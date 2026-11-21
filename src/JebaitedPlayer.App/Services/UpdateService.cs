using System.Reflection;
using JebaitedPlayer.Infrastructure;
using Velopack;
using Velopack.Sources;

namespace JebaitedPlayer.Services;

public enum UpdateState
{
    /// <summary>Razvojna ili kopirana verzija: nadogradnje rade samo u instaliranoj (ili prijenosnoj) verziji.</summary>
    NotInstalled,
    Idle,
    Checking,
    Downloading,
    UpToDate,

    /// <summary>Nova verzija je preuzeta; instalira se pri zatvaranju playera (ili odmah uz ponovno pokretanje).</summary>
    Ready,
    Failed,
}

/// <summary>
/// Nadogradnje preko GitHub Releases (Velopack). Provjera i preuzimanje idu u pozadini; nova verzija se instalira kad
/// korisnik zatvori player, a postavke, povijest gledanja i ključevi su u %LOCALAPPDATA%\jebaited_player, izvan mape
/// programa, pa ih nadogradnja ne dira.
/// </summary>
public sealed class UpdateService
{
    private readonly UpdateManager? _manager;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VelopackAsset? _ready;
    private UpdateState _state;
    private string _status = string.Empty;

    /// <param name="sourceOverride">Mapa s izdanjima umjesto GitHuba (samo za testove).</param>
    /// <param name="store">Inačica iz Microsoft Storea (zadano: prepoznaje se iz Windowsa). Tada nadogradnje radi Store.</param>
    public UpdateService(string? sourceOverride = null, bool? store = null)
    {
        IsStore = store ?? AppPackage.IsStore;
        string? repo = IsStore ? null : RepositoryUrl;
        try
        {
            IUpdateSource? source = IsStore ? null : sourceOverride != null
                ? new SimpleFileSource(new DirectoryInfo(sourceOverride))
                : repo != null ? new GithubSource(repo, null, false) : null;
            if (source != null)
            {
                _manager = new UpdateManager(source);
            }
        }
        catch (Exception ex)
        {
            Log.Updates.Warn($"Nadogradnje nisu dostupne: {ex.Message}");
        }

        _ready = Safe(() => _manager?.UpdatePendingRestart);
        _state = !IsInstalled ? UpdateState.NotInstalled : _ready != null ? UpdateState.Ready : UpdateState.Idle;
        _status = DescribeState();
    }

    /// <summary>GitHub repozitorij s izdanjima (iz Directory.Build.props).</summary>
    public static string? RepositoryUrl { get; } = typeof(UpdateService).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "UpdateRepositoryUrl")?.Value is { Length: > 0 } url ? url : null;

    public event EventHandler? Changed;

    /// <summary>Inačica iz Microsoft Storea: nema Velopacka ni provjere na GitHubu.</summary>
    public bool IsStore { get; }

    public bool IsInstalled => Safe(() => _manager?.IsInstalled) == true;

    public bool IsPortable => Safe(() => _manager?.IsPortable) == true;

    public string CurrentVersion => Safe(() => _manager?.CurrentVersion?.ToString()) ?? AppPaths.Version.Split('-')[0];

    public string? ReadyVersion => _ready?.Version?.ToString();

    public UpdateState State => _state;

    /// <summary>Verzija koja se upravo preuzima (samo dok je stanje Downloading).</summary>
    public string? DownloadVersion { get; private set; }

    /// <summary>Koliko se bajtova preuzima (paket razlika kad postoji, inače cijeli paket).</summary>
    public long DownloadTotalBytes { get; private set; }

    /// <summary>Napredak preuzimanja, 0-100.</summary>
    public int DownloadPercent { get; private set; }

    /// <summary>Veličina za prikaz, npr. "114,8 MB".</summary>
    public static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 * 1024 ? (bytes / 1024.0 / 1024 / 1024).ToString("0.0", Loc.Culture) + " GB"
        : bytes >= 1024 * 1024 ? (bytes / 1024.0 / 1024).ToString("0.0", Loc.Culture) + " MB"
        : Math.Max(1, bytes / 1024).ToString("0", Loc.Culture) + " KB";

    /// <summary>Tekst za postavke, npr. "Nova verzija 1.0.1 je preuzeta…".</summary>
    /// <summary>Opis stanja na trenutnom jeziku (čita se svaki put, pa prati promjenu jezika).</summary>
    public string Status => DescribeState();

    /// <summary>
    /// Provjeri i (ako postoji) preuzmi novu verziju. Bez mreže ili kad GitHub ne odgovara samo zapiše u log.
    /// Vraća verziju koja je spremna za instalaciju (ili null).
    /// </summary>
    public async Task<string?> CheckAndDownloadAsync(CancellationToken ct = default)
    {
        if (_manager is null || !IsInstalled)
        {
            return null;
        }

        if (!await _gate.WaitAsync(0, ct))
        {
            return ReadyVersion;
        }

        try
        {
            SetState(UpdateState.Checking);
            UpdateInfo? info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                SetState(_ready != null ? UpdateState.Ready : UpdateState.UpToDate);
                return ReadyVersion;
            }

            if (_ready != null && _ready.Version == info.TargetFullRelease.Version)
            {
                SetState(UpdateState.Ready);
                return ReadyVersion;
            }

            Log.Updates.Info($"Nadogradnja: dostupna verzija {info.TargetFullRelease.Version} (trenutna {CurrentVersion}), preuzimam.");
            DownloadVersion = info.TargetFullRelease.Version.ToString();
            DownloadTotalBytes = info.DeltasToTarget is { Length: > 0 } deltas ? deltas.Sum(d => d.Size) : info.TargetFullRelease.Size;
            DownloadPercent = 0;
            SetState(UpdateState.Downloading);
            await _manager.DownloadUpdatesAsync(info, percent =>
            {
                int clamped = Math.Clamp(percent, 0, 100);
                if (clamped != DownloadPercent)
                {
                    DownloadPercent = clamped;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }, ct).ConfigureAwait(false);
            DownloadPercent = 100;
            _ready = info.TargetFullRelease;
            Log.Updates.Info($"Nadogradnja: verzija {_ready.Version} preuzeta, instalira se pri zatvaranju playera.");
            SetState(UpdateState.Ready);
            return ReadyVersion;
        }
        catch (OperationCanceledException)
        {
            SetState(_ready != null ? UpdateState.Ready : UpdateState.Idle);
            return ReadyVersion;
        }
        catch (Exception ex)
        {
            Log.Updates.Warn($"Nadogradnja: provjera nije uspjela ({ex.GetType().Name}: {ex.Message})");
            SetState(_ready != null ? UpdateState.Ready : UpdateState.Failed);
            return ReadyVersion;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Pri zatvaranju playera: Velopack pričeka da se proces zatvori i tiho instalira preuzetu verziju.
    /// Sljedeće pokretanje je nova verzija.
    /// </summary>
    /// <param name="restart">Nakon instalacije ponovno pokreni player (gumb "Nadogradi sada" u postavkama).</param>
    public bool ApplyOnExit(bool restart = false)
    {
        if (_manager is null || _ready is null)
        {
            return false;
        }

        try
        {
            _manager.WaitExitThenApplyUpdates(_ready, silent: true, restart: restart);
            Log.Updates.Info($"Nadogradnja na {_ready.Version} pokrenuta (nakon zatvaranja{(restart ? ", uz ponovno pokretanje" : string.Empty)}).");
            return true;
        }
        catch (Exception ex)
        {
            Log.Updates.Error("Nadogradnja pri zatvaranju", ex);
            return false;
        }
    }

    private void SetState(UpdateState state)
    {
        _state = state;
        _status = DescribeState();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string DescribeState() => _state switch
    {
        UpdateState.NotInstalled => Loc.T(IsStore ? "update.store" : "update.notInstalled"),
        UpdateState.Checking => Loc.T("update.checking"),
        UpdateState.Downloading => DownloadVersion is { } v
            ? Loc.T("update.downloadingVersion", v, FormatSize(DownloadTotalBytes))
            : Loc.T("update.downloading"),
        UpdateState.UpToDate => Loc.T("update.upToDate"),
        UpdateState.Ready => Loc.T("update.downloaded", ReadyVersion),
        UpdateState.Failed => Loc.T("update.failed"),
        _ => string.Empty,
    };

    private static T? Safe<T>(Func<T?> get)
    {
        try
        {
            return get();
        }
        catch (Exception)
        {
            return default;
        }
    }
}
