using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using JebaitedPlayer.ViewModels;
using static JebaitedPlayer.Interop.NativeMethods;

namespace JebaitedPlayer.Views;

/// <summary>
/// Prozirni prozor s kontrolama koji leži preko donjeg dijela videa.
/// <para>
/// Poseban prozor je potreban jer WPF ne može crtati preko Win32 prozora u koji mpv crta
/// (tzv. "airspace" ograničenje). Prozor se nikad ne aktivira (WS_EX_NOACTIVATE), pa fokus
/// tipkovnice uvijek ostaje u glavnom prozoru.
/// </para>
/// </summary>
public partial class ControlsOverlay : Window
{
    private readonly PlayerViewModel _vm;

    public ControlsOverlay(PlayerViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;

        SourceInitialized += (_, _) =>
        {
            nint h = new WindowInteropHelper(this).Handle;
            nint ex = GetWindowLongPtr(h, GWL_EXSTYLE);
            SetWindowLongPtr(h, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };

        PreviewMouseMove += (_, _) => UserActivity?.Invoke(this, EventArgs.Empty);
        PreviewMouseWheel += (_, e) =>
        {
            _vm.ChangeVolume(e.Delta > 0 ? 5 : -5);
            UserActivity?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };

        // Vremensku traku vodimo sami: klik skoči na mjesto, povlačenje premotava uživo.
        Timeline.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _vm.IsScrubbing = true;
            Timeline.CaptureMouse();
            ScrubToMouse(e);
            e.Handled = true;
        };
        Timeline.PreviewMouseMove += (_, e) =>
        {
            if (_vm.IsScrubbing && Timeline.IsMouseCaptured)
            {
                ScrubToMouse(e);
            }
        };
        Timeline.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_vm.IsScrubbing)
            {
                ScrubToMouse(e);
                EndScrub();
                e.Handled = true;
            }
        };
        Timeline.LostMouseCapture += (_, _) => EndScrub();
    }

    public event EventHandler? UserActivity;
    public event EventHandler<string[]>? FilesDropped;

    /// <summary>Klik na gumb titlova; argument je gumb (za smještaj izbornika).</summary>
    public event EventHandler<FrameworkElement>? SubtitleMenuRequested;

    private void OnSubtitlesClick(object sender, RoutedEventArgs e) => SubtitleMenuRequested?.Invoke(this, SubtitlesButton);

    /// <summary>Klik na gumb audio trake ili brzine; argument je gumb (za smještaj izbornika).</summary>
    public event EventHandler<FrameworkElement>? AudioMenuRequested;

    public event EventHandler<FrameworkElement>? SpeedMenuRequested;

    private void OnAudioClick(object sender, RoutedEventArgs e) => AudioMenuRequested?.Invoke(this, AudioButton);

    private void OnSpeedClick(object sender, RoutedEventArgs e) => SpeedMenuRequested?.Invoke(this, SpeedButton);

    /// <summary>Klik na gumb popisa epizoda.</summary>
    public event EventHandler? PlaylistToggleRequested;

    private void OnPlaylistClick(object sender, RoutedEventArgs e) => PlaylistToggleRequested?.Invoke(this, EventArgs.Empty);

    public nint Handle => new WindowInteropHelper(this).EnsureHandle();

    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            FilesDropped?.Invoke(this, files);
        }

        e.Handled = true;
    }

    private void ScrubToMouse(MouseEventArgs e)
    {
        if (Timeline.Template.FindName("PART_Track", Timeline) is not Track track)
        {
            return;
        }

        double value = Math.Clamp(track.ValueFromPoint(e.GetPosition(track)), Timeline.Minimum, Timeline.Maximum);
        _vm.ScrubTo(value, final: false);
    }

    private void EndScrub()
    {
        if (!_vm.IsScrubbing)
        {
            return;
        }

        _vm.IsScrubbing = false;
        _vm.ScrubTo(_vm.Position, final: true);
        if (Timeline.IsMouseCaptured)
        {
            Timeline.ReleaseMouseCapture();
        }
    }
}
