using System.Windows.Threading;
using JebaitedPlayer.Infrastructure;
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
            Notify($"Nova verzija {u.ReadyVersion} je preuzeta. Instalirat će se kad zatvoriš player.");
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
        _updates?.ApplyOnExit(_restartAfterUpdate);
    }
}
