namespace JebaitedPlayer.Torlink;

public enum TorlinkStatus
{
    Disabled,
    NotInstalled,
    Ready,
    Starting,
    Running,
    Failed,
}

public enum AddResult
{
    Added,
    Duplicate,
    AlreadyDownloaded,
    NoSpace,
    Invalid,
    TooLarge,
    NotRunning,
    Unauthorized,
    Failed,
}

/// <summary>
/// Jedini ulaz za sučelje. Drži nadzornika, pretragu, ažuriranje i zadnji pregled preuzimanja. Proces ne radi dok ga nitko ne traži
/// (stranica otvorena, nedovršena preuzimanja ili dijeljenje) i gasi se nakon <see cref="IdleTimeout"/> mirovanja.
/// </summary>
public sealed class TorlinkHost : IDisposable
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(2);

    private readonly TorlinkPaths _paths;
    private readonly Func<TorlinkOptions> _options;
    private readonly TimeProvider _clock;
    private readonly StateReader _state;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private DateTimeOffset? _idleSince;
    private bool _pageOpen;

    public TorlinkHost(TorlinkPaths paths, Func<TorlinkOptions> options, IProcessLauncher launcher, IReleaseFeed feed, TimeProvider? clock = null,
        Func<int, string, ApiClient>? apiFactory = null)
    {
        _paths = paths;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _state = new StateReader(paths);
        Store = new RuntimeStore(paths);
        Store.RecoverIncompleteInstall();
        Supervisor = new Supervisor(paths, launcher, _clock, apiFactory);
        Search = new SearchRunner(paths, launcher);
        Updater = new Updater(paths, Store, feed);
        Supervisor.StateChanged += () => Changed?.Invoke();
    }

    public RuntimeStore Store { get; }
    public Supervisor Supervisor { get; }
    public SearchRunner Search { get; }
    public Updater Updater { get; }
    public DownloadSnapshot Snapshot { get; private set; } = DownloadSnapshot.Empty;
    public DiskGuard.Result? LastDiskCheck { get; private set; }
    public TimeSpan IdleTimeout { get; set; } = DefaultIdleTimeout;

    /// <summary>Isključen (zadano): nikakav proces, nikakva mreža.</summary>
    public bool Enabled { get; set; }

    public event Action? Changed;

    public TorlinkStatus Status
    {
        get
        {
            if (!Enabled)
            {
                return TorlinkStatus.Disabled;
            }

            if (Store.Installed() is null)
            {
                return TorlinkStatus.NotInstalled;
            }

            return Supervisor.State switch
            {
                SupervisorState.Starting => TorlinkStatus.Starting,
                SupervisorState.Running => TorlinkStatus.Running,
                SupervisorState.Failed => TorlinkStatus.Failed,
                _ => TorlinkStatus.Ready,
            };
        }
    }

    public RuntimeManifest? Installed => Store.Installed();

    public void PageOpened() => _pageOpen = true;

    public void PageClosed()
    {
        _pageOpen = false;
        _idleSince = null;
    }

    public async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
    {
        if (!Enabled || Store.Installed() is null)
        {
            return false;
        }

        if (Supervisor.State == SupervisorState.Running)
        {
            return true;
        }

        bool ok = await Supervisor.StartAsync(_options(), ct).ConfigureAwait(false);
        Changed?.Invoke();
        return ok;
    }

    /// <summary>Pri pokretanju playera: proces se diže samo ako Torlink ima nedovršena preuzimanja koja treba nastaviti.</summary>
    public async Task<bool> StartIfWorkPendingAsync(CancellationToken ct = default)
    {
        if (!Enabled || Store.Installed() is null || !_state.HasResumableWork())
        {
            return false;
        }

        return await EnsureRunningAsync(ct).ConfigureAwait(false);
    }

    public async Task<SearchResultSet> SearchAsync(string query, string? category, CancellationToken ct = default)
    {
        if (!Enabled)
        {
            throw new TorlinkSearchException(SearchFailure.NoRuntime);
        }

        SearchResultSet set = await Search.SearchAsync(query, category, _options(), 20, ct).ConfigureAwait(false);
        LastSources = set.Sources;
        return set;
    }

    /// <summary>Status izvora iz zadnje pretrage (za Postavke → Torlink: "status povezanih servisa").</summary>
    public IReadOnlyList<SourceStatus> LastSources { get; private set; } = [];

    /// <summary>Osvježi pregled preuzimanja (API + datoteke stanja) i primijeni pravilo dijeljenja.</summary>
    public async Task<DownloadSnapshot> RefreshAsync(CancellationToken ct = default)
    {
        await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ApiStatus status = ApiStatus.Empty;
            ApiClient? api = Supervisor.State == SupervisorState.Running ? Supervisor.Api : null;
            if (api is not null)
            {
                try
                {
                    status = await api.GetStatusAsync(ct).ConfigureAwait(false);
                }
                catch (TorlinkApiException)
                {
                    // proces upravo pada; nadzornik će ga ponovno pokrenuti. Zadržavamo zadnje poznato stanje.
                    return Snapshot;
                }

                if (!_options().SeedAfter)
                {
                    // Dijeljenje isključeno: svaku stavku koja je počela dijeliti odmah zaustavi (datoteke ostaju).
                    foreach (ApiSeed s in status.Seeds.Where(s => s.Status == "seeding"))
                    {
                        await api.ControlAsync(s.Id, "stop-seed", ct).ConfigureAwait(false);
                    }
                }
            }

            (IReadOnlyDictionary<string, QueueEntry> queue, IReadOnlyList<HistoryEntry> history) = await Task.Run(() => (_state.ReadQueue(), _state.ReadHistory())).ConfigureAwait(false);
            Snapshot = DownloadSnapshot.Build(status, queue, history);
            UpdateIdle(status);
            Changed?.Invoke();
            return Snapshot;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void UpdateIdle(ApiStatus status)
    {
        bool seeding = _options().SeedAfter && status.Seeds.Any(s => s.Status == "seeding");
        bool busy = _pageOpen || Snapshot.HasWork || seeding;
        _idleSince = busy ? null : _idleSince ?? _clock.GetUtcNow();
    }

    /// <summary>Periodični poziv (npr. svake 2 s dok je stranica otvorena, rjeđe inače): osvježi i ugasi proces nakon mirovanja.</summary>
    public async Task TickAsync(CancellationToken ct = default)
    {
        if (!Enabled)
        {
            return;
        }

        if (Supervisor.State == SupervisorState.Running)
        {
            await RefreshAsync(ct).ConfigureAwait(false);
            if (_idleSince is { } since && _clock.GetUtcNow() - since >= IdleTimeout)
            {
                await Supervisor.StopAsync().ConfigureAwait(false);
                _idleSince = null;
                Snapshot = DownloadSnapshot.Build(ApiStatus.Empty, _state.ReadQueue(), _state.ReadHistory());
                Changed?.Invoke();
            }
        }
    }

    public async Task<AddResult> AddResultAsync(TorrentResult result, CancellationToken ct = default)
    {
        string hash = result.InfoHash.ToLowerInvariant();
        if (Snapshot.IsCompleted(hash))
        {
            return AddResult.AlreadyDownloaded;
        }

        if (Snapshot.Contains(hash))
        {
            return AddResult.Duplicate;
        }

        LastDiskCheck = DiskGuard.Check(_options().EffectiveDownloadDir, result.SizeBytes);
        if (!LastDiskCheck.Enough)
        {
            return AddResult.NoSpace;
        }

        return await AddMagnetCoreAsync(result.Magnet, ct).ConfigureAwait(false);
    }

    /// <summary>Magnet, goli infohash ili putanja do .torrent datoteke (kako ih Torlink inače prima).</summary>
    public async Task<AddResult> AddInputAsync(string input, CancellationToken ct = default)
    {
        string s = TorlinkInput.Clean(input);
        switch (TorlinkInput.Classify(s))
        {
            case InputKind.Magnet:
            case InputKind.InfoHash:
                string? hash = TorlinkInput.InfoHashOf(s);
                if (hash is not null && Snapshot.IsCompleted(hash))
                {
                    return AddResult.AlreadyDownloaded;
                }

                if (hash is not null && Snapshot.Contains(hash))
                {
                    return AddResult.Duplicate;
                }

                return await AddMagnetCoreAsync(s, ct).ConfigureAwait(false);
            case InputKind.TorrentFile:
                byte[] bytes;
                try
                {
                    bytes = await File.ReadAllBytesAsync(s, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return AddResult.Invalid;
                }

                if (bytes.Length > ApiClient.MaxTorrentBytes)
                {
                    return AddResult.TooLarge;
                }

                if (!await EnsureRunningAsync(ct).ConfigureAwait(false) || Supervisor.Api is not { } api)
                {
                    return AddResult.NotRunning;
                }

                return await MapAsync(() => api.AddTorrentFileAsync(bytes, ct), ct).ConfigureAwait(false);
            default:
                return AddResult.Invalid;
        }
    }

    private async Task<AddResult> AddMagnetCoreAsync(string magnet, CancellationToken ct)
    {
        if (!await EnsureRunningAsync(ct).ConfigureAwait(false) || Supervisor.Api is not { } api)
        {
            return AddResult.NotRunning;
        }

        return await MapAsync(() => api.AddMagnetAsync(magnet, ct), ct).ConfigureAwait(false);
    }

    private async Task<AddResult> MapAsync(Func<Task<AddOutcome>> call, CancellationToken ct)
    {
        try
        {
            AddOutcome o = await call().ConfigureAwait(false);
            if (o == AddOutcome.Added)
            {
                await RefreshAsync(ct).ConfigureAwait(false);
            }

            return o switch
            {
                AddOutcome.Added => AddResult.Added,
                AddOutcome.Invalid => AddResult.Invalid,
                AddOutcome.TooLarge => AddResult.TooLarge,
                AddOutcome.Unauthorized => AddResult.Unauthorized,
                _ => AddResult.Failed,
            };
        }
        catch (TorlinkApiException)
        {
            return AddResult.Failed;
        }
    }

    /// <summary>pause, resume, remove (zadrži datoteke), delete (obriši i datoteke), start-seed, stop-seed.</summary>
    public async Task<bool> ControlAsync(string id, string action, CancellationToken ct = default)
    {
        if (Supervisor.Api is not { } api || Supervisor.State != SupervisorState.Running)
        {
            return false;
        }

        try
        {
            bool ok = await api.ControlAsync(id, action, ct).ConfigureAwait(false);
            await RefreshAsync(ct).ConfigureAwait(false);
            return ok;
        }
        catch (TorlinkApiException)
        {
            return false;
        }
    }

    /// <summary>Isključivanje i zatvaranje playera: proces se gasi, Torlink sam nastavlja iz svog stanja pri sljedećem pokretanju.</summary>
    public async Task ShutdownAsync()
    {
        await Supervisor.StopAsync().ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task<UpdateResult> UpdateAsync(RuntimeRelease release, IProgress<UpdateProgress>? progress, CancellationToken ct = default)
    {
        bool wasRunning = Supervisor.State == SupervisorState.Running;
        UpdateResult r = await Updater.InstallAsync(release, progress, Supervisor.StopAsync, HealthCheckAsync, ct).ConfigureAwait(false);
        if (wasRunning && Enabled && Store.Installed() is not null)
        {
            await EnsureRunningAsync(ct).ConfigureAwait(false);
        }

        Changed?.Invoke();
        return r;
    }

    /// <summary>Zdravstvena provjera nove verzije: proces se diže, odgovara na /health i odmah se gasi.</summary>
    public async Task<bool> HealthCheckAsync()
    {
        bool ok = await Supervisor.StartAsync(_options()).ConfigureAwait(false);
        await Supervisor.StopAsync().ConfigureAwait(false);
        return ok;
    }

    public async Task<bool> RollbackAsync()
    {
        await Supervisor.StopAsync().ConfigureAwait(false);
        bool ok = await Task.Run(Store.Rollback).ConfigureAwait(false);
        Changed?.Invoke();
        return ok;
    }

    public void Dispose()
    {
        Supervisor.Dispose();
        _refreshGate.Dispose();
    }
}
