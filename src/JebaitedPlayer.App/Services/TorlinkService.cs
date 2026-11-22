#if TORLINK
using System.Text.Json;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Torlink;

namespace JebaitedPlayer.Services;

public enum TorlinkEnableResult
{
    Ok,
    NoRuntimePublished,
    NetworkError,
    InstallFailed,
}

/// <summary>
/// Veza između playera i Torlink modula (samo GitHub inačica): vlasnik <see cref="TorlinkHost"/>-a, uključivanje (preuzimanje i provjera
/// runtimea), isključivanje, primjena promijenjenih postavki i zapis u dnevnik. Jedan primjerak po procesu (<see cref="Current"/>).
/// </summary>
public sealed class TorlinkService : IDisposable
{
    private readonly AppSettings _settings;
    private string? _appliedKey;
    private TorlinkStatus? _lastLogged;

    private TorlinkService(AppSettings settings, TorlinkHost host)
    {
        _settings = settings;
        Host = host;
        Host.Enabled = settings.Torlink.Enabled;
        Host.Changed += OnHostChanged;
    }

    public static TorlinkService? Current { get; private set; }

    public TorlinkHost Host { get; }

    public TorlinkSettings Settings => _settings.Torlink;

    /// <summary>Uključen i instaliran: tek tada se na Homeu vidi "Pretraži online".</summary>
    public bool IsAvailable => Settings.Enabled && Host.Installed is not null;

    public event Action? Changed;

    public static TorlinkService Create(AppSettings settings)
    {
        var paths = new TorlinkPaths(AppPaths.DataDir);
        IReleaseFeed feed = CreateFeed();
        var host = new TorlinkHost(paths, () => settings.Torlink.ToOptions(), new ProcessLauncher(), feed);
        Current = new TorlinkService(settings, host);
        return Current;
    }

    /// <summary>GitHub; samo u testnom načinu (JEBAITED_DATA_DIR) i uz JEBAITED_TORLINK_API moguć je lokalni poslužitelj izdanja.</summary>
    private static IReleaseFeed CreateFeed()
    {
        string? test = Environment.GetEnvironmentVariable("JEBAITED_TORLINK_API");
        if (!string.IsNullOrEmpty(test) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR")))
        {
            string b = test.TrimEnd('/');
            return new GitHubReleaseFeed(upstreamApi: b + "/upstream", runtimeApi: b + "/runtime", allowLocalHttp: true);
        }

        return new GitHubReleaseFeed();
    }

    private void OnHostChanged()
    {
        TorlinkStatus now = Host.Status;
        if (now != _lastLogged)
        {
            _lastLogged = now;
            Log.Info($"Torlink: stanje {now}{(Host.Supervisor.LastError is { } e ? " (" + e + ")" : string.Empty)}.");
        }

        if (Host.Status == TorlinkStatus.Running)
        {
            _appliedKey ??= OptionsKey();
        }
        else if (Host.Status != TorlinkStatus.Starting)
        {
            _appliedKey = null;
        }

        Changed?.Invoke();
    }

    private string OptionsKey() => JsonSerializer.Serialize(Settings.ToOptions());

    /// <summary>Uključi: (ako treba) preuzmi i provjeri runtime, zatim označi kao uključen. Prihvat upozorenja radi pozivatelj prije ovoga.</summary>
    public async Task<TorlinkEnableResult> EnableAsync(IProgress<UpdateProgress>? progress, CancellationToken ct = default)
    {
        if (Host.Installed is null)
        {
            UpdateInfo info = await Host.Updater.CheckAsync(ct).ConfigureAwait(false);
            if (info.Runtime is null)
            {
                Log.Info($"Torlink: nema runtime paketa za preuzimanje ({info.Error ?? "nema izdanja"}).");
                return info.Error is not null ? TorlinkEnableResult.NetworkError : TorlinkEnableResult.NoRuntimePublished;
            }

            UpdateResult r = await Host.UpdateAsync(info.Runtime, progress, ct).ConfigureAwait(false);
            if (!r.Success)
            {
                Log.Info($"Torlink: instalacija nije uspjela ({r.Step}, {r.Detail}).");
                return TorlinkEnableResult.InstallFailed;
            }

            Settings.LastUpdate = DateTime.UtcNow.ToString("o");
            Log.Info($"Torlink: instaliran {Host.Installed?.Display}.");
        }

        Settings.Enabled = true;
        Host.Enabled = true;
        Log.Info("Torlink uključen.");
        Changed?.Invoke();
        return TorlinkEnableResult.Ok;
    }

    /// <summary>Isključi: proces se gasi (preuzimanja ostaju na disku i nastavljaju se pri sljedećem uključivanju), postavke ostaju.</summary>
    public async Task DisableAsync()
    {
        Settings.Enabled = false;
        Host.Enabled = false;
        await Host.ShutdownAsync().ConfigureAwait(false);
        Log.Info("Torlink isključen.");
        Changed?.Invoke();
    }

    /// <summary>Nakon spremanja postavki: ako se promijenilo nešto što Torlink čita pri pokretanju, ponovno ga pokreni (preuzimanja se nastavljaju).</summary>
    public async Task ApplyOptionsAsync()
    {
        if (!Settings.Enabled || Host.Status != TorlinkStatus.Running || _appliedKey is null || _appliedKey == OptionsKey())
        {
            return;
        }

        Log.Info("Torlink: postavke su promijenjene, ponovno pokretanje.");
        _appliedKey = null;
        await Host.Supervisor.StopAsync().ConfigureAwait(false);
        await Host.EnsureRunningAsync().ConfigureAwait(false);
    }

    public async Task<UpdateInfo> CheckForUpdateAsync(CancellationToken ct = default)
    {
        UpdateInfo info = await Host.Updater.CheckAsync(ct).ConfigureAwait(false);
        if (info.Error != "both")
        {
            Settings.LastUpdateCheck = DateTime.UtcNow.ToString("o");
        }

        return info;
    }

    public async Task<UpdateResult> InstallUpdateAsync(RuntimeRelease release, IProgress<UpdateProgress>? progress, CancellationToken ct = default)
    {
        UpdateResult r = await Host.UpdateAsync(release, progress, ct).ConfigureAwait(false);
        if (r.Success)
        {
            Settings.LastUpdate = DateTime.UtcNow.ToString("o");
        }

        Log.Info($"Torlink: ažuriranje {(r.Success ? "uspjelo" : "nije uspjelo")} ({r.Step}{(r.RolledBack ? ", vraćena prethodna verzija" : string.Empty)}).");
        return r;
    }

    public Task<bool> StartIfWorkPendingAsync() => Host.StartIfWorkPendingAsync();

    public void Dispose() => Host.Dispose();
}
#endif
