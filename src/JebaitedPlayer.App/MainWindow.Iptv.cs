using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Iptv;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>IPTV: panel s izvorima, kanalima i EPG-om lijevo od videa u glavnom prozoru. Kanal svira u istom playeru (isti mpv kao radio i datoteke).</summary>
public partial class MainWindow
{
    private IptvPanel? _iptv;
    private bool _iptvDockWasVisible;

    internal IptvPanel? IptvPanelRef => _iptv;

    internal bool IsIptvVisible => IptvDock.Visibility == Visibility.Visible;

    /// <summary>Isključen modul ne prikazuje izbornik ni sučelje i ne pokreće nikakve zadatke.</summary>
    internal bool IptvEnabled
    {
        get
        {
            try
            {
                return new IptvStore(AppPaths.DataDir).LoadSettings().Enabled;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }
    }

    /// <summary>Prikaži (ili sakrij ako je već prikazan) IPTV panel. Iz mini playera prvo se vraća puni prozor.</summary>
    internal void ShowIptv(Window? owner = null)
    {
        if (IsIptvVisible)
        {
            HideIptv();
            return;
        }

        if (IsMiniPlayerActive)
        {
            ((IMiniPlayerHost)this).ShowFullPlayer();
        }

        if (_vm.IsFullscreen)
        {
            SetFullscreen(false);
        }

        if (_iptv is null)
        {
            _iptv = new IptvPanel(new IptvStore(AppPaths.DataDir), PlayIptvStream,
                () => new RadioPlayback(_vm.CurrentPath, _vm.IsLoading, _vm.Position, _vm.ErrorMessage));
            _iptv.CloseRequested += HideIptv;
            // Dok se rub povlači, područje videa (početni ekran / video) zadržava veličinu pa se samo pomiče: slaganje cijelog početnog
            // ekrana i promjena veličine videa pri svakoj sličici na velikom zaslonu trzaju. Jedno slaganje je na kraju povlačenja.
            _iptv.PanelDragStarted += () =>
            {
                VideoArea.HorizontalAlignment = HorizontalAlignment.Left;
                VideoArea.Width = VideoArea.ActualWidth;
            };
            _iptv.PanelDragCompleted += () =>
            {
                VideoArea.ClearValue(FrameworkElement.WidthProperty);
                VideoArea.HorizontalAlignment = HorizontalAlignment.Stretch;
                UpdateOverlayBounds();
            };
            _iptv.PanelWidthRequested += w => IptvDock.Width = Math.Clamp(w, 380, Math.Max(380, ActualWidth - 340));
            IptvDock.Width = Math.Clamp(_iptv.SavedPanelWidth, 380, Math.Max(380, ActualWidth - 340));
            IptvDock.Child = _iptv;
        }

        IptvDock.Visibility = Visibility.Visible;
        UpdateHomeRailActive();
        _ = _iptv.StartAsync();
        UpdateOverlayBounds();
    }

    /// <summary>Sakrij panel (reprodukcija se ne prekida); video dobiva cijelu širinu.</summary>
    internal void HideIptv()
    {
        IptvDock.Visibility = Visibility.Collapsed;
        UpdateHomeRailActive();
        UpdateOverlayBounds();
    }

    private void PlayIptvStream(string url, string name)
    {
        if (!TryEnsurePlayer())
        {
            return;
        }

        _nextLoadIsVideo = true;
        _userOpenPending = true;
        _vm.Library.DetachForStream();
        _vm.Load(url, startPaused: false, displayTitle: name);
    }
}
