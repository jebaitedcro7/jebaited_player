using System.Windows.Threading;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Library;
using JebaitedPlayer.Services;

namespace JebaitedPlayer;

/// <summary>
/// Nadogradnje: provjera u pozadini (malo nakon pokretanja, zatim svakih 12 sati), preuzimanje bez pitanja,
/// instalacija kad korisnik zatvori player. Ništa se ne prekida usred gledanja.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan FirstUpdateCheck = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(12);

    private UpdateService? _updates;
    private DispatcherTimer? _updateTimer;
    private bool _updateNoticeShown;
    private bool _restartAfterUpdate;

    internal UpdateService Updates => _updates ??= new UpdateService(_options.DiagUpdateSource);

    private void InitializeUpdates()
    {
        UpdateService updates = Updates;
        updates.Changed += (_, _) => Dispatcher.BeginInvoke(OnUpdateStateChanged);
        Log.Info($"Nadogradnje: {(updates.IsInstalled ? $"verzija {updates.CurrentVersion}{(updates.IsPortable ? " (prijenosna)" : string.Empty)}" : "nije instalirana verzija, provjera isključena")}");

        if (_options.IsDiagnostics)
        {
            // Dijagnostika provjerava samo uz zadanu mapu s izdanjima (test nadogradnje), nikad GitHub.
            if (_diag != null && _options.DiagUpdateSource != null)
            {
                _diag.UpdateProbe = async () =>
                {
                    string? ready = await updates.CheckAndDownloadAsync();
                    return new Dictionary<string, object?>
                    {
                        ["installed"] = updates.IsInstalled,
                        ["portable"] = updates.IsPortable,
                        ["current"] = updates.CurrentVersion,
                        ["ready"] = ready,
                        ["state"] = updates.State.ToString(),
                        ["status"] = updates.Status,
                        ["dataDir"] = AppPaths.DataDir,
                        ["settingsVolume"] = _settings.Volume,
                    };
                };
            }

            return;
        }

        if (!updates.IsInstalled)
        {
            return;
        }

        if (updates.State == UpdateState.Ready)
        {
            OnUpdateStateChanged();
        }

        _updateTimer = new DispatcherTimer(FirstUpdateCheck, DispatcherPriority.Background, async (_, _) =>
        {
            _updateTimer!.Interval = UpdateInterval;
            if (_settings.AutoUpdate && !_closing)
            {
                await updates.CheckAndDownloadAsync();
            }
        }, Dispatcher);
        _updateTimer.Start();
    }

    private void OnUpdateStateChanged()
    {
        if (_updates is { State: UpdateState.Ready } u && !_updateNoticeShown && !_closing)
        {
            _updateNoticeShown = true;
            Notify(Loc.T("update.downloaded", u.ReadyVersion));
        }
    }

    /// <summary>Gumb u postavkama: zatvori normalno (spremi poziciju i postavke), instaliraj i ponovno pokreni.</summary>
    internal void RestartForUpdate()
    {
        _restartAfterUpdate = true;
        Close();
    }

    /// <summary>Zadnji korak zatvaranja: preuzeta verzija se instalira kad ovaj proces završi.</summary>
    private void ApplyUpdateOnExit()
    {
        _updateTimer?.Stop();
        if (_updates?.ReadyVersion is { } version && BackupsAllowed(_options))
        {
            // M1a: kopija postavki i povijesti prije svake nadogradnje (baza je već zatvorena, pa se kopira datoteka).
            try
            {
                SettingsBackup.Create(AppPaths.DataDir, store: null, "update-" + version, DateTime.UtcNow);
                Log.Info($"Kopija postavki prije nadogradnje na {version} napravljena.");
            }
            catch (Exception ex)
            {
                Log.Error("Kopija postavki prije nadogradnje", ex);
            }
        }

        _updates?.ApplyOnExit(_restartAfterUpdate);
    }

    /// <summary>Automatske kopije: uvijek u normalnom radu; u dijagnostici samo s testnom mapom podataka (nikad tvoji podaci).</summary>
    private static bool BackupsAllowed(StartupOptions options) =>
        !options.IsDiagnostics || Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is { Length: > 0 };

    /// <summary>M1a: pri pokretanju, u pozadini, jednom dnevno.</summary>
    private void StartDailyBackup(StartupOptions options)
    {
        if (!BackupsAllowed(options))
        {
            return;
        }

        WatchStore? store = options.IsDiagnostics ? null : _store; // dijagnostika ima privremenu bazu: kopira se baza iz testne mape
        _ = Task.Run(() =>
        {
            try
            {
                if (SettingsBackup.CreateIfDue(AppPaths.DataDir, store, DateTime.UtcNow) is { } b)
                {
                    Log.Info($"Dnevna kopija postavki i povijesti: {Path.GetFileName(b.Folder)}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or Microsoft.Data.Sqlite.SqliteException)
            {
                Log.Warn($"Dnevna kopija postavki nije napravljena: {ex.Message}");
            }
        });
    }
}
