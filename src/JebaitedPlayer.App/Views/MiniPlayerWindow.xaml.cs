using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.ViewModels;

namespace JebaitedPlayer.Views;

/// <summary>Što mini player traži od glavnog prozora (isti mpv, ista biblioteka, iste postavke).</summary>
internal interface IMiniPlayerHost
{
    void ShowFullPlayer();

    void ClosePlayer();

    void ShowOpenDialog(Window owner);

    void OpenFiles(IReadOnlyList<string> paths);

    void ApplyPlaybackSettings();

    bool HandleShortcut(Key key, ModifierKeys mods);

    string? GetMpvProperty(string name);
}

/// <summary>
/// Mini Music Player (ciklus 2, V1). Puni izgled je dizajn 1 (Klasični Winamp: LCD s vremenom i VU metrom,
/// pokretni naslov, tipke, glasnoća, EQ i popis), a uska traka je dizajn 3. Veličina se mijenja ručkom dolje desno,
/// Ctrl + kotačićem ili Ctrl + / −. Svira isti mpv kao glavni player, pa prebacivanje ne prekida glazbu.
/// </summary>
public partial class MiniPlayerWindow : Window
{
    private const double VuFloorDb = -60;

    private readonly PlayerViewModel _vm;
    private readonly AppSettings _settings;
    private readonly IMiniPlayerHost _host;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _vuTimer;
    private bool _showRemaining;
    private bool _seeking;
    private bool _updatingVolume;
    private bool _vuBusy;
    private int _ticks;
    private double _vuLeft = VuFloorDb;
    private double _vuRight = VuFloorDb;
    private string _marquee = string.Empty;
    private Point _gripStart;
    private double _gripScale;
    private double _gripWidth;

    /// <summary>Glavni prozor se zatvara: mini player se zatvara s njim (inače × znači „zatvori player”).</summary>
    internal bool AllowClose { get; set; }

    internal MiniPlayerWindow(PlayerViewModel vm, AppSettings settings, IMiniPlayerHost host)
    {
        _vm = vm;
        _settings = settings;
        _host = host;
        InitializeComponent();
        DataContext = vm.Library;
        Equalizer.Initialize(settings.Equalizer, host.ApplyPlaybackSettings, sliderHeight: 64, columnWidth: 34);

        VolumeSlider.Value = vm.Volume;
        VolumeSlider.ValueChanged += (_, e) =>
        {
            if (!_updatingVolume)
            {
                _vm.Volume = e.NewValue;
                _settings.Volume = _vm.Volume;
            }
        };
        SeekSlider.PreviewMouseLeftButtonDown += (_, _) => _seeking = true;
        SeekSlider.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_seeking && _vm.HasMedia)
            {
                _vm.SeekAbsolute(SeekSlider.Value, exact: true);
            }

            _seeking = false;
        };

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher);
        _vuTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, async (_, _) => await UpdateVuAsync(), Dispatcher);
        _timer.Stop();
        _vuTimer.Stop();
        IsVisibleChanged += (_, _) => OnVisibilityChanged();

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
        LocationChanged += (_, _) =>
        {
            _settings.MiniPlayer.Left = Left;
            _settings.MiniPlayer.Top = Top;
        };
        PreviewKeyDown += OnKey;
        PreviewMouseWheel += OnWheel;
        Drop += OnDrop;
        Closing += OnClosingWindow;
        MarqueeBox.SizeChanged += (_, _) => StartMarquee(MarqueeBox, MarqueeText);
        CompactMarqueeBox.SizeChanged += (_, _) => StartMarquee(CompactMarqueeBox, CompactMarqueeText);
        this.OnLanguageChanged(() =>
        {
            Equalizer.Refresh();
            _marquee = string.Empty;
            Tick();
        });

        ApplyLayout();
        PlaceOnScreen();
    }

    internal double Scale => Zoom.ScaleX;

    internal bool IsCompact => _settings.MiniPlayer.Compact;

    // --- Izgled -------------------------------------------------------------------------------------------------

    /// <summary>Puni izgled ili uska traka, ploče EQ i popisa, povećanje.</summary>
    internal void ApplyLayout()
    {
        MiniPlayerSettings m = _settings.MiniPlayer;
        FullView.Visibility = m.Compact ? Visibility.Collapsed : Visibility.Visible;
        CompactView.Visibility = m.Compact ? Visibility.Visible : Visibility.Collapsed;
        EqToggle.IsChecked = m.ShowEqualizer;
        PlaylistToggle.IsChecked = m.ShowPlaylist;
        EqHost.Visibility = m.ShowEqualizer ? Visibility.Visible : Visibility.Collapsed;
        PlaylistHost.Visibility = m.ShowPlaylist ? Visibility.Visible : Visibility.Collapsed;
        SetScale(m.Scale);
    }

    internal void SetScale(double scale)
    {
        scale = Math.Clamp(Math.Round(scale, 2), MiniPlayerSettings.MinScale, MiniPlayerSettings.MaxScale);
        Zoom.ScaleX = scale;
        Zoom.ScaleY = scale;
        _settings.MiniPlayer.Scale = scale;
    }

    internal void SetCompact(bool compact)
    {
        _settings.MiniPlayer.Compact = compact;
        ApplyLayout();
        _marquee = string.Empty;
        Tick();
    }

    internal void SetPanels(bool equalizer, bool playlist)
    {
        _settings.MiniPlayer.ShowEqualizer = equalizer;
        _settings.MiniPlayer.ShowPlaylist = playlist;
        ApplyLayout();
    }

    /// <summary>Zapamćen položaj ako je na nekom zaslonu, inače dolje desno na radnoj površini.</summary>
    private void PlaceOnScreen()
    {
        MiniPlayerSettings m = _settings.MiniPlayer;
        Rect screen = new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (m.Left is double left && m.Top is double top && screen.Contains(new Point(left + 40, top + 20)))
        {
            Left = left;
            Top = top;
            return;
        }

        Rect work = SystemParameters.WorkArea;
        Left = work.Right - 480 * m.Scale - 24;
        Top = work.Bottom - 140 * m.Scale - 24;
    }

    private void OnCompact(object sender, RoutedEventArgs e) => SetCompact(true);

    private void OnExpand(object sender, RoutedEventArgs e) => SetCompact(false);

    private void OnCompactEqualizer(object sender, RoutedEventArgs e)
    {
        _settings.MiniPlayer.ShowEqualizer = true;
        SetCompact(false);
    }

    private void OnPanelsChanged(object sender, RoutedEventArgs e) => SetPanels(EqToggle.IsChecked == true, PlaylistToggle.IsChecked == true);

    private void OnFullPlayer(object sender, RoutedEventArgs e) => _host.ShowFullPlayer();

    private void OnClosePlayer(object sender, RoutedEventArgs e) => _host.ClosePlayer();

    private void OnClosingWindow(object? sender, CancelEventArgs e)
    {
        if (!AllowClose)
        {
            // Alt+F4 na mini playeru zatvara player (kao ×), ne samo mini prozor.
            e.Cancel = true;
            Dispatcher.BeginInvoke(_host.ClosePlayer);
        }
    }

    // --- Ručna promjena veličine ----------------------------------------------------------------------------------

    private void OnGripStarted(object sender, DragStartedEventArgs e)
    {
        _gripStart = PointToScreen(Mouse.GetPosition(this));
        _gripScale = Scale;
        _gripWidth = ActualWidth;
    }

    private void OnGripDelta(object sender, DragDeltaEventArgs e)
    {
        Point now = PointToScreen(Mouse.GetPosition(this));
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double dx = (now.X - _gripStart.X) / dpi;
        if (_gripWidth > 0)
        {
            SetScale(_gripScale * (_gripWidth + dx) / _gripWidth);
        }
    }

    private void OnGripCompleted(object sender, DragCompletedEventArgs e) => Log.Info($"Mini player: veličina {Scale:P0}");

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            SetScale(Scale + (e.Delta > 0 ? 0.1 : -0.1));
            e.Handled = true;
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers == ModifierKeys.Control && key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract or Key.D0 or Key.NumPad0)
        {
            SetScale(key is Key.D0 or Key.NumPad0 ? 1 : Scale + (key is Key.OemPlus or Key.Add ? 0.1 : -0.1));
            e.Handled = true;
            return;
        }

        // Ostale tipke rade isto kao u glavnom playeru (razmak, strelice, M, N, Ctrl+M natrag…).
        e.Handled = _host.HandleShortcut(key, Keyboard.Modifiers);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            _host.OpenFiles(files);
        }
    }

    // --- Reprodukcija -------------------------------------------------------------------------------------------

    private void OnPrevious(object sender, RoutedEventArgs e) => _vm.Library.PlayPrevious();

    private void OnNext(object sender, RoutedEventArgs e) => _vm.Library.PlayNext();

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_vm.HasMedia)
        {
            _vm.SetPaused(false);
        }
        else if (_vm.Library.Playlist.FirstOrDefault() is { } first)
        {
            _vm.Library.PlayItem(first);
        }
        else
        {
            _host.ShowOpenDialog(this);
        }
    }

    private void OnPause(object sender, RoutedEventArgs e) => _vm.TogglePause();

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (_vm.HasMedia)
        {
            _vm.TogglePause();
        }
        else
        {
            OnPlay(sender, e);
        }
    }

    /// <summary>Stop kao u Winampu: pjesma ostaje učitana, na početku i pauzirana.</summary>
    private void OnStop(object sender, RoutedEventArgs e)
    {
        if (_vm.HasMedia)
        {
            _vm.SetPaused(true);
            _vm.SeekAbsolute(0, exact: true);
        }
    }

    private void OnOpen(object sender, RoutedEventArgs e) => _host.ShowOpenDialog(this);

    private void OnMute(object sender, RoutedEventArgs e) => _vm.ToggleMute();

    private void OnTimeClick(object sender, MouseButtonEventArgs e)
    {
        _showRemaining = !_showRemaining;
        e.Handled = true;
        Tick();
    }

    // --- Prikaz -------------------------------------------------------------------------------------------------

    private void OnVisibilityChanged()
    {
        if (IsVisible)
        {
            _marquee = string.Empty;
            Tick();
            _timer.Start();
            _vuTimer.Start();
        }
        else
        {
            _timer.Stop();
            _vuTimer.Stop();
            StopMarquee(MarqueeText);
            StopMarquee(CompactMarqueeText);
        }
    }

    /// <summary>Svakih 250 ms: vrijeme, traka, tipka play/pauza, glasnoća; naslov i kvaliteta svake sekunde.</summary>
    internal void Tick()
    {
        bool has = _vm.HasMedia && _vm.Duration > 0;
        double position = has ? _vm.Position : 0;
        double duration = has ? _vm.Duration : 0;
        double left = Math.Max(0, duration - position);
        TimeText.Text = _showRemaining && has ? "-" + Short(left) : Short(position);
        RemainingText.Text = has ? "-" + Short(left) : string.Empty;
        SeekSlider.IsEnabled = has;
        SeekSlider.Maximum = Math.Max(1, duration);
        if (!_seeking)
        {
            SeekSlider.Value = position;
        }

        CompactProgress.Value = has ? position / duration : 0;
        CompactTime.Text = has ? $"{Short(position)} / {Short(duration)}" : string.Empty;
        CompactPlayPause.Content = has && !_vm.IsPaused ? "" : "";
        string muteGlyph = _vm.IsMuted || _vm.Volume <= 0 ? "" : "";
        MuteButton.Content = muteGlyph;
        CompactMute.Content = muteGlyph;
        if (!VolumeSlider.IsMouseCaptureWithin && Math.Abs(VolumeSlider.Value - _vm.Volume) > 0.5)
        {
            _updatingVolume = true;
            VolumeSlider.Value = _vm.Volume;
            _updatingVolume = false;
        }

        if (!has)
        {
            SetMarquee(_vm.HasMedia ? string.Empty : Loc.T("mini.empty"));
        }
        else if (_ticks++ % 4 == 0 || _marquee.Length == 0)
        {
            _ = UpdateTitleAsync();
        }
    }

    /// <summary>„Izvođač — Naslov  ···  320 kbps · 48 kHz” iz oznaka datoteke (mpv).</summary>
    private async Task UpdateTitleAsync()
    {
        (string? artist, string? title, string? bitrate, string? rate) = await Task.Run(() => (
            _host.GetMpvProperty("metadata/by-key/Artist"),
            _host.GetMpvProperty("media-title"),
            _host.GetMpvProperty("audio-bitrate"),
            _host.GetMpvProperty("audio-params/samplerate")));
        string name = title is { Length: > 0 } t ? t : System.IO.Path.GetFileNameWithoutExtension(_vm.CurrentPath ?? string.Empty);
        if (artist is { Length: > 0 } a && !name.Contains(a, StringComparison.OrdinalIgnoreCase))
        {
            name = a + " — " + name;
        }

        var quality = new List<string>();
        if (double.TryParse(bitrate, NumberStyles.Float, CultureInfo.InvariantCulture, out double bps) && bps > 0)
        {
            quality.Add($"{Math.Round(bps / 1000):0} kbps");
        }

        if (double.TryParse(rate, NumberStyles.Float, CultureInfo.InvariantCulture, out double hz) && hz > 0)
        {
            quality.Add((hz / 1000).ToString("0.#", CultureInfo.InvariantCulture) + " kHz");
        }

        SetMarquee(quality.Count > 0 ? $"{name}  ···  {string.Join(" · ", quality)}" : name);
    }

    internal string MarqueeTextValue => _marquee;

    private void SetMarquee(string text)
    {
        if (text == _marquee)
        {
            return;
        }

        _marquee = text;
        MarqueeText.Text = text;
        CompactMarqueeText.Text = text;
        StartMarquee(MarqueeBox, MarqueeText);
        StartMarquee(CompactMarqueeBox, CompactMarqueeText);
    }

    /// <summary>Predug naslov polako klizi lijevo-desno (20 sličica u sekundi, malo CPU-a); kratak stoji.</summary>
    private void StartMarquee(FrameworkElement box, TextBlock text)
    {
        StopMarquee(text);
        if (!IsVisible || box.ActualWidth <= 0)
        {
            return;
        }

        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double overflow = text.DesiredSize.Width - box.ActualWidth;
        if (overflow <= 2)
        {
            return;
        }

        var animation = new DoubleAnimation(0, -overflow - 8, TimeSpan.FromSeconds(1 + overflow / 25))
        {
            BeginTime = TimeSpan.FromSeconds(1.5),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Timeline.SetDesiredFrameRate(animation, 20);
        ((TranslateTransform)text.RenderTransform).BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private static void StopMarquee(TextBlock text)
    {
        var shift = (TranslateTransform)text.RenderTransform;
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        shift.X = 0;
    }

    /// <summary>VU metar: stvarna razina (RMS, dB) lijevog i desnog kanala iz mpv-a; brzo raste, polako pada.</summary>
    private async Task UpdateVuAsync()
    {
        if (_vuBusy)
        {
            return;
        }

        _vuBusy = true;
        try
        {
            double l = VuFloorDb, r = VuFloorDb;
            if (_vm.HasMedia && !_vm.IsPaused)
            {
                (string? a, string? b) = await Task.Run(() => (
                    _host.GetMpvProperty("af-metadata/vu/by-key/lavfi.astats.1.RMS_level"),
                    _host.GetMpvProperty("af-metadata/vu/by-key/lavfi.astats.2.RMS_level")));
                l = ParseDb(a);
                r = b is null ? l : ParseDb(b);
            }

            _vuLeft = l >= _vuLeft ? l : Math.Max(l, _vuLeft - 2.5);
            _vuRight = r >= _vuRight ? r : Math.Max(r, _vuRight - 2.5);
            VuLeft.Width = VuWidth(_vuLeft);
            VuRight.Width = VuWidth(_vuRight);
        }
        finally
        {
            _vuBusy = false;
        }
    }

    internal (double Left, double Right) VuLevels => (_vuLeft, _vuRight);

    private double VuWidth(double db) => Math.Max(0, VuTrackL.ActualWidth * (Math.Clamp(db, VuFloorDb, 0) - VuFloorDb) / -VuFloorDb);

    private static double ParseDb(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double db) && double.IsFinite(db) ? db : VuFloorDb;

    private static string Short(double seconds) => TimeFormat.Format(seconds, seconds >= 3600);
}
