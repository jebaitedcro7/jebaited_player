using System.Windows;
using System.Windows.Input;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Mini Music Player (ciklus 2): glavni prozor se sakrije, a mini prozor upravlja istim mpv-om i bibliotekom,
/// pa se glazba ne prekida. Ctrl+M (ili izbornik) prebacuje naprijed-natrag.
/// </summary>
public partial class MainWindow : IMiniPlayerHost
{
    private MiniPlayerWindow? _mini;

    internal bool IsMiniPlayerActive => _mini is { IsVisible: true };

    internal MiniPlayerWindow? MiniPlayer => _mini;

    internal void ToggleMiniPlayer()
    {
        if (IsMiniPlayerActive)
        {
            ((IMiniPlayerHost)this).ShowFullPlayer();
        }
        else
        {
            ShowMiniPlayer();
        }
    }

    internal void ShowMiniPlayer()
    {
        if (_vm.IsFullscreen)
        {
            SetFullscreen(false);
        }

        _mini ??= new MiniPlayerWindow(_vm, _settings, this);
        MpvConfig.VuMeter = true;
        ((ISettingsHost)this).ApplyPlaybackSettings();
        _mini.Show();
        _mini.Activate();
        Hide();
        UpdateOverlayVisibility();
        Log.Info("Mini player otvoren.");
    }

    void IMiniPlayerHost.ShowFullPlayer()
    {
        _mini?.Hide();
        MpvConfig.VuMeter = false;
        ((ISettingsHost)this).ApplyPlaybackSettings();
        Show();
        Activate();
        UpdateOverlayVisibility();
        Log.Info("Mini player zatvoren, natrag na puni player.");
    }

    void IMiniPlayerHost.ClosePlayer() => Close();

    void IMiniPlayerHost.ShowOpenDialog(Window owner) => ShowOpenDialog(owner);

    void IMiniPlayerHost.OpenFiles(IReadOnlyList<string> paths) => OpenFiles(paths);

    void IMiniPlayerHost.ApplyPlaybackSettings() => ((ISettingsHost)this).ApplyPlaybackSettings();

    bool IMiniPlayerHost.HandleShortcut(Key key, ModifierKeys mods) => HandleShortcut(key, mods);

    string? IMiniPlayerHost.GetMpvProperty(string name) => _player?.GetPropertyString(name);

    /// <summary>Zatvaranje playera: mini prozor se zatvara s njim.</summary>
    private void CloseMiniPlayer()
    {
        if (_mini is { } mini)
        {
            mini.AllowClose = true;
            mini.Close();
            _mini = null;
        }

        MpvConfig.VuMeter = false;
    }
}
