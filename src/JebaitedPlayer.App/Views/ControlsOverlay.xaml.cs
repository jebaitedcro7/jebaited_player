using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using JebaitedPlayer.Infrastructure;
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

        // 2.1.7: crvena oznaka LIVE dok radio stvarno svira.
        void UpdateLive() => LiveMark.Visibility = _vm.IsLiveStream && !_vm.IsPaused ? Visibility.Visible : Visibility.Collapsed;
        vm.PropertyChanged += (_, _) => UpdateLive();
        UpdateLive();

        PreviewMouseMove += (_, _) => UserActivity?.Invoke(this, EventArgs.Empty);
        // 2.1.7: samo iznad trake glasnoće; kotačić drugdje ne smije slučajno mijenjati glasnoću.
        VolumeArea.PreviewMouseWheel += (_, e) =>
        {
            _vm.ChangeVolumeByWheel(e.Delta);
            _vm.ShowOsd(Loc.T("osd.volume", _vm.Volume));
            UserActivity?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };

        // Vremensku traku vodimo sami (UPGRADE 3): klik bilo gdje odmah precizno premota na to mjesto,
        // povlačenje premotava uživo (ključni kadrovi), a na kraju povlačenja opet precizno.
        // Klik se ovdje "potroši", pa nikad ne stigne do videa (nema pauze ni pokretanja).
        Timeline.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (!_vm.HasMedia || _vm.Duration <= 0)
            {
                return;
            }

            _vm.IsScrubbing = true;
            _scrubMoved = false;
            _scrubStartX = e.GetPosition(Timeline).X;
            Timeline.CaptureMouse();
            double value = ValueAt(e);
            _vm.ScrubTo(value, final: true);
            ShowPreview(e, value);
            UserActivity?.Invoke(this, EventArgs.Empty);
        };
        Timeline.PreviewMouseMove += (_, e) =>
        {
            if (_vm.IsScrubbing && Timeline.IsMouseCaptured)
            {
                if (Math.Abs(e.GetPosition(Timeline).X - _scrubStartX) >= 3)
                {
                    _scrubMoved = true;
                }

                if (_scrubMoved)
                {
                    double value = ValueAt(e);
                    _vm.ScrubTo(value, final: false);
                    ShowPreview(e, value);
                }
            }
            else if (_vm.Duration > 0)
            {
                ShowPreview(e, ValueAt(e));
            }
        };
        Timeline.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_vm.IsScrubbing)
            {
                if (_scrubMoved)
                {
                    _vm.ScrubTo(ValueAt(e), final: true);
                }

                EndScrub();
                e.Handled = true;
            }
        };
        Timeline.LostMouseCapture += (_, _) => EndScrub();
        Timeline.MouseLeave += (_, _) =>
        {
            if (!_vm.IsScrubbing)
            {
                SeekPreview.Visibility = Visibility.Collapsed;
            }
        };
    }

    private bool _scrubMoved;
    private double _scrubStartX;

    /// <summary>Oblačić s vremenom iznad trake (UPGRADE 3): vidljiv dok je miš na traci; dijagnostika ga čita.</summary>
    internal string? SeekPreviewText => SeekPreview.Visibility == Visibility.Visible ? SeekBubbleText.Text : null;

    /// <summary>Točno vrijeme (s) iz oblačića: tamo vodi klik na tom mjestu.</summary>
    internal double? SeekPreviewSeconds => SeekPreview.Visibility == Visibility.Visible ? _previewSeconds : null;

    private double _previewSeconds;

    /// <summary>Točka na zaslonu (fizički pikseli) za zadani udio trake 0–1: ondje klik vodi na udio × trajanje.</summary>
    internal Point TimelineScreenPoint(double fraction)
    {
        Track track = TimelineTrack ?? throw new InvalidOperationException("Traka nije spremna.");
        double thumb = track.Thumb?.ActualWidth ?? 0;
        double x = thumb / 2 + (track.ActualWidth - thumb) * Math.Clamp(fraction, 0, 1);
        return track.PointToScreen(new Point(x, track.ActualHeight / 2));
    }

    private Track? TimelineTrack => Timeline.Template?.FindName("PART_Track", Timeline) as Track;

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

    /// <summary>Vrijeme (s) ispod miša: isto preslikavanje kao kružić trake (sredina kružića je na mišu).</summary>
    private double ValueAt(MouseEventArgs e)
    {
        if (TimelineTrack is not { } track)
        {
            return _vm.Position;
        }

        return Math.Clamp(track.ValueFromPoint(e.GetPosition(track)), Timeline.Minimum, Timeline.Maximum);
    }

    private void ShowPreview(MouseEventArgs e, double value)
    {
        if (TimelineTrack is not { } track || _vm.Duration <= 0)
        {
            SeekPreview.Visibility = Visibility.Collapsed;
            return;
        }

        double thumb = track.Thumb?.ActualWidth ?? 0;
        double x = Math.Clamp(e.GetPosition(Timeline).X, thumb / 2, Math.Max(thumb / 2, Timeline.ActualWidth - thumb / 2));
        _previewSeconds = value;
        SeekBubbleText.Text = TimeFormat.Format(value, _vm.Duration >= 3600);
        SeekPreview.Visibility = Visibility.Visible;
        SeekBubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = SeekBubble.DesiredSize.Width;
        Canvas.SetLeft(SeekBubble, Math.Clamp(x - width / 2, -40, Math.Max(-40, Timeline.ActualWidth + 40 - width)));
        Canvas.SetLeft(SeekMarker, x - 1);
    }

    private void EndScrub()
    {
        if (!_vm.IsScrubbing)
        {
            return;
        }

        _vm.IsScrubbing = false;
        if (Timeline.IsMouseCaptured)
        {
            Timeline.ReleaseMouseCapture();
        }

        if (!Timeline.IsMouseOver)
        {
            SeekPreview.Visibility = Visibility.Collapsed;
        }
    }
}
