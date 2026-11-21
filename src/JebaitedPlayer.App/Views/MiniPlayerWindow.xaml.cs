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

    void RefreshAudioFilters();

    void ShowSettings();

    void ShowRadio(Window owner);

    /// <summary>Ciklus 5, V8: Spotify daljinski (prozor).</summary>
    void ShowSpotify(Window owner);

    /// <summary>Ciklus 5, V8: stanje i upravljanje Spotifyjem (dok je on izvor, tipke mini playera upravljaju njime).</summary>
    JebaitedPlayer.Services.SpotifyRemote Spotify { get; }

    /// <summary>Ciklus 6, V9: YouTube Music (prozor s ugrađenim playerom).</summary>
    void ShowYouTube(Window owner);

    JebaitedPlayer.Services.YouTubeRemote YouTube { get; }
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
        Equalizer.Initialize(CurrentEqualizer, host.ApplyPlaybackSettings, sliderHeight: 64, columnWidth: 34);
        ContextMenuOpening += (_, _) => ContextMenu = BuildMenu();
        ContextMenu = BuildMenu();

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
            BindEqualizer();
            _marquee = string.Empty;
            Tick();
        });

        ApplyLayout();
        ApplyDisplayStyle();
        BindEqualizer();
        PlaceOnScreen();
    }

    // --- 1.4.1: equalizer prema sadržaju, font, VU, izbornik desnog klika ------------------------------------

    /// <summary>Equalizer koji sada vrijedi: glazba i radio imaju svoj, filmovi i serije svoj.</summary>
    private EqualizerSettings CurrentEqualizer => MpvConfig.MusicMode ? _settings.MusicEqualizer : _settings.Equalizer;

    internal void BindEqualizer()
    {
        Equalizer.Bind(CurrentEqualizer);
        EqTitle.Text = Loc.T(MpvConfig.MusicMode ? "mini.eqMusic" : "mini.eqMovies");
    }

    /// <summary>Font zaslona i izgled VU metra (desni klik ili Postavke → Izgled).</summary>
    internal void ApplyDisplayStyle()
    {
        MiniPlayerSettings m = _settings.MiniPlayer;
        var family = new FontFamily(MiniPlayerSettings.FontFamilyFor(m.Font));
        TimeText.FontFamily = family;
        MarqueeText.FontFamily = family;
        CompactMarqueeText.FontFamily = family;
        Vu.VuStyle = m.VuStyle;
        VuLabels.Visibility = m.VuStyle == "off" ? Visibility.Hidden : Visibility.Visible;
        _marquee = string.Empty;
        if (IsVisible)
        {
            Tick();
        }
    }

    internal void SetFont(string id)
    {
        _settings.MiniPlayer.Font = id;
        ApplyDisplayStyle();
    }

    internal void SetVuStyle(string id)
    {
        _settings.MiniPlayer.VuStyle = id;
        ApplyDisplayStyle();
        _vuLeft = _vuRight = VuFloorDb;
        Vu.SetLevels(VuFloorDb, VuFloorDb);
        _host.RefreshAudioFilters();
    }

    private ContextMenu BuildMenu()
    {
        MiniPlayerSettings m = _settings.MiniPlayer;
        var menu = new ContextMenu();
        var fonts = new MenuItem { Header = Loc.T("mini.menuFont") };
        foreach (var (id, _) in MiniPlayerSettings.Fonts)
        {
            fonts.Items.Add(Check(Loc.T("miniFont." + id), m.Font == id, () => SetFont(id)));
        }

        var vu = new MenuItem { Header = Loc.T("mini.menuVu") };
        foreach (string id in MiniPlayerSettings.VuStyles)
        {
            vu.Items.Add(Check(Loc.T("vu." + id), m.VuStyle == id, () => SetVuStyle(id)));
        }

        var size = new MenuItem { Header = Loc.T("mini.menuSize") };
        foreach (double s in new[] { 0.75, 1, 1.25, 1.5, 2, 2.5 })
        {
            size.Items.Add(Check($"{s * 100:0} %", Math.Abs(Scale - s) < 0.01, () => SetScale(s)));
        }

        menu.Items.Add(fonts);
        menu.Items.Add(vu);
        menu.Items.Add(size);
        menu.Items.Add(new Separator());
        menu.Items.Add(Check(Loc.T("mini.compact"), m.Compact, () => SetCompact(!m.Compact)));
        menu.Items.Add(Check(Loc.T("mini.equalizer"), m.ShowEqualizer && !m.Compact, () => { m.Compact = false; SetPanels(!m.ShowEqualizer, m.ShowPlaylist); }));
        menu.Items.Add(Check(Loc.T("mini.playlist"), m.ShowPlaylist && !m.Compact, () => { m.Compact = false; SetPanels(m.ShowEqualizer, !m.ShowPlaylist); }));
        menu.Items.Add(Check(Loc.T("mini.autoSwitch"), m.AutoSwitch, () => m.AutoSwitch = !m.AutoSwitch));
        menu.Items.Add(new Separator());
        menu.Items.Add(Check(Loc.T("menu.radio"), false, () => _host.ShowRadio(this)));
        menu.Items.Add(Check(Loc.T("mini.settings"), false, _host.ShowSettings));
        menu.Items.Add(Check(Loc.T("mini.fullPlayer"), false, _host.ShowFullPlayer));
        menu.Items.Add(Check(Loc.T("mini.close"), false, _host.ClosePlayer));
        return menu;
    }

    private static MenuItem Check(string header, bool isChecked, Action action)
    {
        var item = new MenuItem { Header = header.Replace("_", "__", StringComparison.Ordinal), IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
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

    /// <summary>V8: Spotify je izvor (pušten iz prozora Spotify), pa tipke upravljaju njime dok se ne pusti nešto lokalno.</summary>
    private bool SpotifyMode => _host.Spotify.IsActiveSource;

    /// <summary>V9: YouTube je izvor (svira u prozoru YouTube Music), pa tipke idu ugrađenom playeru.</summary>
    private bool YouTubeMode => _host.YouTube.IsActiveSource;

    private void OnPrevious(object sender, RoutedEventArgs e)
    {
        if (YouTubeMode)
        {
            _ = _host.YouTube.PreviousAsync();
            return;
        }

        if (SpotifyMode)
        {
            _ = _host.Spotify.PreviousAsync();
            return;
        }

        _vm.Library.PlayPrevious();
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (YouTubeMode)
        {
            _ = _host.YouTube.NextAsync();
            return;
        }

        if (SpotifyMode)
        {
            _ = _host.Spotify.NextAsync();
            return;
        }

        _vm.Library.PlayNext();
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (YouTubeMode)
        {
            _ = _host.YouTube.SendAsync("player.playVideo()");
            return;
        }

        if (SpotifyMode)
        {
            if (_host.Spotify.State is not { IsPlaying: true })
            {
                _ = _host.Spotify.TogglePlayAsync();
            }

            return;
        }

        if (_vm.HasMedia || _vm.StoppedStream != null)
        {
            // Radio (R1) se ponovno spaja uživo, a datoteka samo nastavlja.
            _vm.Play();
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

    private void OnPause(object sender, RoutedEventArgs e)
    {
        if (YouTubeMode)
        {
            _ = _host.YouTube.TogglePlayAsync();
            return;
        }

        if (SpotifyMode)
        {
            _ = _host.Spotify.TogglePlayAsync();
            return;
        }

        // Pauza dok ništa ne svira ne pokreće zaustavljenu postaju (to radi Play).
        if (_vm.HasMedia)
        {
            _vm.TogglePause();
        }
    }

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (YouTubeMode)
        {
            _ = _host.YouTube.TogglePlayAsync();
            return;
        }

        if (SpotifyMode)
        {
            _ = _host.Spotify.TogglePlayAsync();
            return;
        }

        if (_vm.HasMedia || _vm.StoppedStream != null)
        {
            _vm.TogglePause();
        }
        else
        {
            OnPlay(sender, e);
        }
    }

    /// <summary>
    /// Stop kao u Winampu: pjesma ostaje učitana, na početku i pauzirana. Radio (R1) se stvarno zaustavlja,
    /// a Play ga ponovno spaja uživo (prije je „početak” streama bio trenutak uključenja postaje).
    /// </summary>
    private void OnStop(object sender, RoutedEventArgs e)
    {
        if (YouTubeMode)
        {
            _ = _host.YouTube.PauseAsync();
            return;
        }

        if (SpotifyMode)
        {
            _ = _host.Spotify.PauseAsync();
            return;
        }

        _vm.StopPlayback();
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
    private bool _spotifyConsumer;

    internal void Tick()
    {
        // V8: dok je Spotify izvor i mini player vidljiv, stanje se osvježava (inače nema mreže ni timera).
        bool spotify = SpotifyMode;
        if ((spotify && IsVisible) != _spotifyConsumer)
        {
            _spotifyConsumer = !_spotifyConsumer;
            if (_spotifyConsumer)
            {
                _host.Spotify.AddConsumer();
            }
            else
            {
                _host.Spotify.RemoveConsumer();
            }
        }

        if (YouTubeMode)
        {
            TickYouTube();
            return;
        }

        if (spotify)
        {
            TickSpotify();
            return;
        }

        bool stream = _vm.IsLiveStream;
        StreamInfo? stopped = _vm.HasMedia ? null : _vm.StoppedStream;
        bool has = _vm.HasMedia && _vm.Duration > 0 && !stream;
        double position = has || stream ? _vm.Position : 0;
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
        SourceText.Text = Loc.T(stream || stopped != null ? "mini.sourceRadio" : "mini.sourceLocal");
        if (stream)
        {
            // Radio uživo: vrijeme slušanja, bez premotavanja i bez preostalog vremena.
            TimeText.Text = Short(position);
            RemainingText.Text = Loc.T("mini.live");
            SeekSlider.IsEnabled = false;
            CompactTime.Text = $"{Short(position)} · {Loc.T("mini.live")}";
        }
        else if (stopped != null)
        {
            // R1: postaja zaustavljena tipkom Stop ostaje prikazana u naslovu; Play je ponovno spaja uživo.
            // (Polje preostalog vremena je preusko za „ZAUSTAVLJENO”, pa ostaje prazno.)
            CompactTime.Text = Loc.T("mini.stopped");
        }
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

        if (stopped != null)
        {
            SetMarquee($"{stopped.Title}  ···  {Loc.T("mini.stopped")}");
        }
        else if (!has && !stream)
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
        bool stream = PlayerViewModel.IsStreamPath(_vm.CurrentPath);
        (string? artist, string? title, string? bitrate, string? rate, string? icy) = await Task.Run(() => (
            _host.GetMpvProperty("metadata/by-key/Artist"),
            _host.GetMpvProperty("media-title"),
            _host.GetMpvProperty("audio-bitrate"),
            _host.GetMpvProperty("audio-params/samplerate"),
            stream ? _host.GetMpvProperty("metadata/by-key/icy-title") : null));
        string name;
        if (stream)
        {
            // Radio: „Postaja — trenutna pjesma” (ICY oznaka streama, ako je postaja šalje).
            name = _vm.Title is { Length: > 0 } station ? station : title ?? string.Empty;
            if (icy is { Length: > 0 } song && !name.Contains(song, StringComparison.OrdinalIgnoreCase))
            {
                name += " — " + song;
            }
        }
        else
        {
            name = title is { Length: > 0 } t ? t : System.IO.Path.GetFileNameWithoutExtension(_vm.CurrentPath ?? string.Empty);
            if (artist is { Length: > 0 } a && !name.Contains(a, StringComparison.OrdinalIgnoreCase))
            {
                name = a + " — " + name;
            }
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
        if (_vuBusy || _settings.MiniPlayer.VuStyle == "off")
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
            Vu.SetLevels(_vuLeft, _vuRight);
        }
        finally
        {
            _vuBusy = false;
        }
    }

    internal (double Left, double Right) VuLevels => (_vuLeft, _vuRight);

    private static double ParseDb(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double db) && double.IsFinite(db) ? db : VuFloorDb;

    private static string Short(double seconds) => TimeFormat.Format(seconds, seconds >= 3600);

    /// <summary>V8: Spotify kao izvor — pjesma, izvođač, vrijeme i oznaka SPOTIFY (tipke idu Spotifyju).</summary>
    private void TickSpotify()
    {
        JebaitedPlayer.Online.SpotifyPlayback? s = _host.Spotify.State;
        double position = (s?.ProgressMs ?? 0) / 1000.0;
        double duration = (s?.DurationMs ?? 0) / 1000.0;
        SourceText.Text = Loc.T("mini.sourceSpotify");
        TimeText.Text = _showRemaining && duration > 0 ? "-" + Short(Math.Max(0, duration - position)) : Short(position);
        RemainingText.Text = duration > 0 ? "-" + Short(Math.Max(0, duration - position)) : string.Empty;
        SeekSlider.IsEnabled = false;
        SeekSlider.Maximum = Math.Max(1, duration);
        SeekSlider.Value = Math.Min(position, SeekSlider.Maximum);
        CompactProgress.Value = duration > 0 ? position / duration : 0;
        CompactTime.Text = duration > 0 ? $"{Short(position)} / {Short(duration)}" : string.Empty;
        CompactPlayPause.Content = s is { IsPlaying: true } ? "" : "";
        string title = s is { Track.Length: > 0 }
            ? (s.Artists.Length > 0 ? $"{s.Artists} — {s.Track}" : s.Track) + (s.DeviceName is { Length: > 0 } d ? $"  ···  {d}" : string.Empty)
            : _host.Spotify.Status is { Length: > 0 } status ? status : Loc.T("spotify.nothingPlaying");
        SetMarquee(title);
    }

    /// <summary>V9: YouTube kao izvor — naslov, vrijeme i oznaka YOUTUBE (tipke idu ugrađenom playeru).</summary>
    private void TickYouTube()
    {
        JebaitedPlayer.Services.YouTubeState? s = _host.YouTube.State;
        double position = s?.CurrentTime ?? 0;
        double duration = s?.Duration ?? 0;
        SourceText.Text = Loc.T("mini.sourceYouTube");
        TimeText.Text = _showRemaining && duration > 0 ? "-" + Short(Math.Max(0, duration - position)) : Short(position);
        RemainingText.Text = duration > 0 ? "-" + Short(Math.Max(0, duration - position)) : string.Empty;
        SeekSlider.IsEnabled = false;
        SeekSlider.Maximum = Math.Max(1, duration);
        SeekSlider.Value = Math.Min(position, SeekSlider.Maximum);
        CompactProgress.Value = duration > 0 ? position / duration : 0;
        CompactTime.Text = duration > 0 ? $"{Short(position)} / {Short(duration)}" : string.Empty;
        CompactPlayPause.Content = s is { IsPlaying: true } ? "\uE769" : "\uE768";
        SetMarquee(s is { Title.Length: > 0 } ? s.Title : _host.YouTube.Status is { Length: > 0 } st ? st : Loc.T("youtube.title"));
    }

    /// <summary>Klik na oznaku izvora: Radio, Spotify ili YouTube Music (ciklusi 5 i 6).</summary>
    private void OnSourceClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var radio = new MenuItem { Header = Loc.T("menu.radio") };
        radio.Click += (_, _) => _host.ShowRadio(this);
        var spotify = new MenuItem { Header = Loc.T("menu.spotify") };
        spotify.Click += (_, _) => _host.ShowSpotify(this);
        var youTube = new MenuItem { Header = Loc.T("menu.youtube") };
        youTube.Click += (_, _) => _host.ShowYouTube(this);
        menu.Items.Add(radio);
        menu.Items.Add(spotify);
        menu.Items.Add(youTube);
        menu.IsOpen = true;
    }
}
