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

    /// <summary>V4-B: bot za Discord (prozor).</summary>
    void ShowDiscord(Window owner);

    /// <summary>V4-B: bot ako je već stvoren (null dok ga korisnik ne otvori), za oznaku stanja u mini playeru.</summary>
    JebaitedPlayer.Services.Discord.DiscordBot? DiscordBot { get; }

    /// <summary>2.1.12: omot trenutne pjesme (zajednički za mini player i Home ekran).</summary>
    JebaitedPlayer.Services.CoverArtLoader Cover { get; }
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

        // 2.0: LCD i VU metar u boji isticanja (zadana zelena = isto kao prije).
        ApplyLcd();
        WindowTheme.AccentChanged += ApplyLcd;
        Closed += (_, _) => WindowTheme.AccentChanged -= ApplyLcd;
        Equalizer.Initialize(CurrentEqualizer, host.ApplyPlaybackSettings, sliderHeight: 64, columnWidth: 34);
        ContextMenuOpening += (_, _) => ContextMenu = BuildMenu();
        ContextMenu = BuildMenu();

        VolumeSlider.Value = vm.Volume;
        AttachVolume(VolumeSlider);
        AttachVolume(CompactVolume);
        AttachSeek(SeekSlider);

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

    // --- 2.1.7: glasnoća kotačićem, premotavanje, izgledi ----------------------------------------------------------

    internal bool IsSeeking => _seeking;

    /// <summary>Traka glasnoće: povlačenje i kotačić miša (samo iznad trake; 0–100 %), isto u svim izgledima.</summary>
    private void AttachVolume(Slider slider)
    {
        slider.ValueChanged += (_, e) =>
        {
            if (!_updatingVolume)
            {
                _vm.Volume = e.NewValue;
                _settings.Volume = _vm.Volume;
            }
        };
        slider.PreviewMouseWheel += (_, e) =>
        {
            _vm.ChangeVolumeByWheel(e.Delta);
            _settings.Volume = _vm.Volume;
            SyncVolume(slider, _vm.Volume); // traka odmah prati kotačić, bez čekanja na sat
            e.Handled = true;
        };
    }

    /// <summary>Postavi traku glasnoće na stvarnu glasnoću (bez povratnog učinka na player).</summary>
    internal void SyncVolume(Slider slider, double volume)
    {
        if (!slider.IsMouseCaptureWithin && Math.Abs(slider.Value - volume) > 0.5)
        {
            _updatingVolume = true;
            slider.Value = volume;
            _updatingVolume = false;
        }
    }

    private void AttachSeek(Slider slider)
    {
        slider.PreviewMouseLeftButtonDown += (_, _) => _seeking = true;
        slider.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_seeking && _vm.HasMedia)
            {
                _vm.SeekAbsolute(slider.Value, exact: true);
            }

            _seeking = false;
        };
    }

    private MiniSkinBase? _skin;
    private string _skinDesign = string.Empty;
    private bool _isLive;
    private MiniSkinState _lastSkinState;

    internal string ActiveDesign => _settings.MiniPlayer.ActiveDesign;

    /// <summary>Izgled koji je trenutno vidljiv (za provjeru).</summary>
    internal MiniSkinBase? CurrentSkin => ActiveDesign is "card" or "side" or "ufo" ? _skin : null;

    /// <summary>Odabir izgleda: classic (1), card (2), strip (3), side (4), ufo (5). Sprema se i pamti.</summary>
    internal void SetDesign(string design)
    {
        MiniPlayerSettings m = _settings.MiniPlayer;
        if (!MiniPlayerSettings.Designs.Contains(design))
        {
            return;
        }

        if (design == "strip")
        {
            m.Compact = true;
        }
        else
        {
            m.Compact = false;
            m.Design = design;
            if (design == "side")
            {
                m.ShowPlaylist = true; // bočna ploča pokazuje popis odmah (dizajn 4)
            }
        }

        ApplyLayout();
        _marquee = string.Empty;
        Tick();
        Log.Info($"Mini player: izgled {design}");
    }

    private MiniSkinBase CreateSkin(string design)
    {
        MiniSkinBase skin = design switch
        {
            "card" => new MiniCardSkin(),
            "side" => new MiniSideSkin(),
            _ => new MiniUfoSkin(),
        };
        skin.Mini = this;
        AttachVolume(skin.VolumeBar);
        AttachSeek(skin.SeekBar);
        ApplySkinFont(skin);
        return skin;
    }

    private void ApplySkinFont(MiniSkinBase skin)
    {
        // Izgledi s LCD zaslonom (NLO) prate odabrani font zaslona.
        if (skin is MiniUfoSkin)
        {
            System.Windows.Documents.TextElement.SetFontFamily(skin.TitleMarquee, new FontFamily(MiniPlayerSettings.FontFamilyFor(_settings.MiniPlayer.Font)));
            skin.TitleMarquee.Refresh();
        }
    }

    /// <summary>Isti podaci u svim izgledima: čita se iz kontrola klasičnog izgleda koje Tick() već održava.</summary>
    private void SyncSkin()
    {
        if (CurrentSkin is not { } skin)
        {
            return;
        }

        MiniPlayerSettings m = _settings.MiniPlayer;
        bool spotify = SpotifyMode, youtube = YouTubeMode;
        bool has = !spotify && !youtube && _vm.HasMedia && _vm.Duration > 0 && !_vm.IsLiveStream;
        double position = spotify || youtube ? SeekSlider.Value : has ? _vm.Position : 0;
        var state = new MiniSkinState(
            Title: _marquee,
            Time: _skinTime,
            Remaining: RemainingText.Text,
            Source: SourceText.Text,
            Position: position,
            Duration: SeekSlider.Maximum,
            CanSeek: SeekSlider.IsEnabled,
            Playing: _skinPlaying,
            MuteGlyph: _vm.IsMuted || _vm.Volume <= 0 ? "" : "",
            Live: _isLive,
            Volume: _vm.Volume,
            EqOn: m.ShowEqualizer,
            PlaylistOn: m.ShowPlaylist,
            Cover: spotify || youtube ? null : _host.Cover.Current);
        _lastSkinState = state;
        skin.Apply(state);
    }

    private string _skinTime = string.Empty;
    private bool _skinPlaying;

    /// <summary>Podaci za provjeru: što izgled prikazuje.</summary>
    internal MiniSkinState LastSkinState => _lastSkinState;

    /// <summary>Font zaslona i izgled VU metra (desni klik ili Postavke → Izgled).</summary>
    internal void ApplyDisplayStyle()
    {
        MiniPlayerSettings m = _settings.MiniPlayer;
        var family = new FontFamily(MiniPlayerSettings.FontFamilyFor(m.Font));
        TimeText.FontFamily = family;
        System.Windows.Documents.TextElement.SetFontFamily(Marquee, family);
        System.Windows.Documents.TextElement.SetFontFamily(CompactMarquee, family);
        Marquee.Refresh();
        CompactMarquee.Refresh();
        if (_skin is not null)
        {
            ApplySkinFont(_skin);
        }

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

        var designs = new MenuItem { Header = Loc.T("mini.menuDesign") };
        foreach (string id in MiniPlayerSettings.Designs)
        {
            designs.Items.Add(Check(Loc.T("miniDesign." + id), m.ActiveDesign == id, () => SetDesign(id)));
        }

        var info = new MenuItem { Header = Loc.T("mini.menuInfo") };
        AddInfoItems(info.Items);

        menu.Items.Add(designs);
        menu.Items.Add(info);
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
        menu.Items.Add(Check(Loc.T("menu.discord"), false, () => _host.ShowDiscord(this)));
        menu.Items.Add(Check(Loc.T("mini.settings"), false, _host.ShowSettings));
        menu.Items.Add(Check(Loc.T("mini.minimize"), false, () => OnMinimize(this, new RoutedEventArgs())));
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

    /// <summary>Odabrani izgled (1–5), ploče EQ i popisa, povećanje.</summary>
    internal void ApplyLayout()
    {
        MiniPlayerSettings m = _settings.MiniPlayer;
        string design = m.ActiveDesign;
        FullView.Visibility = design == "classic" ? Visibility.Visible : Visibility.Collapsed;
        CompactView.Visibility = design == "strip" ? Visibility.Visible : Visibility.Collapsed;
        if (design is "card" or "side" or "ufo")
        {
            if (_skin is null || _skinDesign != design)
            {
                _skin = CreateSkin(design);
                _skinDesign = design;
                SkinHost.Content = _skin;
            }

            SkinHost.Visibility = Visibility.Visible;
        }
        else
        {
            SkinHost.Visibility = Visibility.Collapsed;
        }

        EqToggle.IsChecked = m.ShowEqualizer;
        PlaylistToggle.IsChecked = m.ShowPlaylist;
        bool panels = design != "strip";
        EqHost.Visibility = m.ShowEqualizer && panels ? Visibility.Visible : Visibility.Collapsed;
        PlaylistHost.Visibility = m.ShowPlaylist && panels ? Visibility.Visible : Visibility.Collapsed;
        SetScale(m.Scale);
        if (IsLoaded)
        {
            SyncSkin();
        }
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

    /// <summary>Zapamćen položaj ako je na nekom zaslonu, inače dolje desno na radnoj površini; uvijek cijeli u vidljivom području.</summary>
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

    internal void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCompact(object sender, RoutedEventArgs e) => SetCompact(true);

    private void OnExpand(object sender, RoutedEventArgs e) => SetCompact(false);

    private void OnCompactEqualizer(object sender, RoutedEventArgs e)
    {
        _settings.MiniPlayer.ShowEqualizer = true;
        SetCompact(false);
    }

    private void OnPanelsChanged(object sender, RoutedEventArgs e) => SetPanels(EqToggle.IsChecked == true, PlaylistToggle.IsChecked == true);

    internal void OnFullPlayer(object sender, RoutedEventArgs e) => _host.ShowFullPlayer();

    internal void OnClosePlayer(object sender, RoutedEventArgs e) => _host.ClosePlayer();

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
            return;
        }

        // 2.1.9: kotačić bilo gdje na mini playeru mijenja glasnoću (popis pjesama se i dalje lista kotačićem).
        if (Keyboard.Modifiers == ModifierKeys.None && !e.Handled && !WheelHelper.IsInsideScrollable(e.OriginalSource))
        {
            _vm.ChangeVolumeByWheel(e.Delta);
            _settings.Volume = _vm.Volume;
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

    internal void OnPrevious(object sender, RoutedEventArgs e)
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

    internal void OnNext(object sender, RoutedEventArgs e)
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

    internal void OnPlay(object sender, RoutedEventArgs e)
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

    internal void OnPause(object sender, RoutedEventArgs e)
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

    internal void OnPlayPause(object sender, RoutedEventArgs e)
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
    internal void OnStop(object sender, RoutedEventArgs e)
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

    internal void OnOpen(object sender, RoutedEventArgs e) => _host.ShowOpenDialog(this);

    internal void OnMute(object sender, RoutedEventArgs e) => _vm.ToggleMute();

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
        }
    }

    /// <summary>Svakih 250 ms: vrijeme, traka, tipka play/pauza, glasnoća; naslov i kvaliteta svake sekunde.</summary>
    private bool _spotifyConsumer;

    internal void Tick()
    {
        TickDiscord();

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

        // 2.1.12: omot samo za lokalnu glazbu (Spotify, YouTube i radio imaju NLO logo).
        _host.Cover.Request(spotify || YouTubeMode ? null : _vm.CurrentPath);

        if (YouTubeMode)
        {
            TickYouTube();
            SyncSkin();
            return;
        }

        if (spotify)
        {
            TickSpotify();
            SyncSkin();
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
        _skinTime = TimeText.Text;

        // 2.1.7: LIVE svijetli samo dok radio stvarno svira (ne i kad je pauziran ili zaustavljen).
        _isLive = stream && !_vm.IsPaused;
        if (stream)
        {
            // Radio uživo: vrijeme slušanja, bez premotavanja; oznaka LIVE umjesto preostalog vremena.
            TimeText.Text = Short(position);
            RemainingText.Text = string.Empty;
            SeekSlider.IsEnabled = false;
            CompactTime.Text = Short(position);
            _skinTime = Short(position);
        }
        else if (stopped != null)
        {
            // R1: postaja zaustavljena tipkom Stop ostaje prikazana u naslovu; Play je ponovno spaja uživo.
            // (Polje preostalog vremena je preusko za „ZAUSTAVLJENO”, pa ostaje prazno.)
            CompactTime.Text = Loc.T("mini.stopped");
            _skinTime = Loc.T("mini.stopped");
        }

        LiveClassic.Visibility = LiveCompact.Visibility = _isLive ? Visibility.Visible : Visibility.Collapsed;
        _skinPlaying = _vm.HasMedia && !_vm.IsPaused;
        CompactPlayPause.Content = _skinPlaying ? "\uE769" : "\uE768";
        string muteGlyph = _vm.IsMuted || _vm.Volume <= 0 ? "\uE74F" : "\uE767";
        MuteButton.Content = muteGlyph;
        CompactMute.Content = muteGlyph;
        SyncVolume(VolumeSlider, _vm.Volume);
        SyncVolume(CompactVolume, _vm.Volume);

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

        SyncSkin();
    }

    /// <summary>
    /// Traka s podacima o pjesmi: zadano „Izvođač — Naslov”; ostale podatke (album, godinu, žanr, kvalitetu, datoteku)
    /// korisnik uključuje desnim klikom na traku. Postavka vrijedi za sve izglede.
    /// </summary>
    private async Task UpdateTitleAsync()
    {
        bool stream = PlayerViewModel.IsStreamPath(_vm.CurrentPath);
        string? path = _vm.CurrentPath;
        string title = _vm.Title;
        JebaitedPlayer.Services.TrackMeta meta = await Task.Run(() =>
            JebaitedPlayer.Services.TrackInfo.Read(name => _host.GetMpvProperty(name), path, title, stream));
        SetMarquee(JebaitedPlayer.Services.TrackInfo.Format(meta, _settings.MiniPlayer.InfoFields));
    }

    internal string MarqueeTextValue => _marquee;

    private void SetMarquee(string text)
    {
        if (text == _marquee)
        {
            return;
        }

        _marquee = text;
        Marquee.Text = text;
        CompactMarquee.Text = text;
    }

    /// <summary>Desni klik na traku s pjesmom: izbor podataka koji se prikazuju (izvođač, naziv, album, godina…).</summary>
    internal void OnInfoMenu(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        AddInfoItems(menu.Items);
        menu.IsOpen = true;
    }

    private void AddInfoItems(ItemCollection items)
    {
        foreach (string id in JebaitedPlayer.Services.TrackInfo.AllFields)
        {
            bool on = _settings.MiniPlayer.InfoFields.Contains(id);
            items.Add(Check(Loc.T("info." + id), on, () => ToggleInfoField(id)));
        }

        items.Add(new Separator());
        items.Add(Check(Loc.T("info.reset"), false, () => SetInfoFields(JebaitedPlayer.Services.TrackInfo.DefaultFields)));
    }

    internal void ToggleInfoField(string id)
    {
        List<string> fields = [.. _settings.MiniPlayer.InfoFields];
        if (!fields.Remove(id))
        {
            fields.Add(id);
        }

        SetInfoFields(fields);
    }

    internal void SetInfoFields(IEnumerable<string> fields)
    {
        _settings.MiniPlayer.InfoFields = JebaitedPlayer.Services.TrackInfo.Normalize(fields);
        _ticks = 0;
        _marquee = string.Empty;
        Tick();
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
        _isLive = false;
        LiveClassic.Visibility = LiveCompact.Visibility = Visibility.Collapsed;
        _skinPlaying = s is { IsPlaying: true };
        TimeText.Text = _showRemaining && duration > 0 ? "-" + Short(Math.Max(0, duration - position)) : Short(position);
        _skinTime = TimeText.Text;
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
        _isLive = false;
        LiveClassic.Visibility = LiveCompact.Visibility = Visibility.Collapsed;
        _skinPlaying = s is { IsPlaying: true };
        TimeText.Text = _showRemaining && duration > 0 ? "-" + Short(Math.Max(0, duration - position)) : Short(position);
        _skinTime = TimeText.Text;
        RemainingText.Text = duration > 0 ? "-" + Short(Math.Max(0, duration - position)) : string.Empty;
        SeekSlider.IsEnabled = false;
        SeekSlider.Maximum = Math.Max(1, duration);
        SeekSlider.Value = Math.Min(position, SeekSlider.Maximum);
        CompactProgress.Value = duration > 0 ? position / duration : 0;
        CompactTime.Text = duration > 0 ? $"{Short(position)} / {Short(duration)}" : string.Empty;
        CompactPlayPause.Content = s is { IsPlaying: true } ? "\uE769" : "\uE768";
        SetMarquee(s is { Title.Length: > 0 } ? s.Title : _host.YouTube.Status is { Length: > 0 } st ? st : Loc.T("youtube.title"));
    }

    // --- 2.0: boja isticanja ----------------------------------------------------------------------------------------

    /// <summary>LCD (vrijeme, naslov, oznaka izvora, okviri) i VU metar iz palete trenutne boje isticanja.</summary>
    private void ApplyLcd()
    {
        JebaitedPlayer.Infrastructure.AccentPalette p = WindowTheme.Palette;
        Resources["LcdText"] = WindowTheme.Frozen(p.LcdText);
        Resources["LcdBorder"] = WindowTheme.Frozen(p.LcdBorder);
        Resources["LcdTrack"] = WindowTheme.Frozen(p.LcdTrack);
        Resources["LcdBack"] = WindowTheme.Frozen(p.LcdBack);
        Resources["LcdTag"] = WindowTheme.Frozen(p.LcdTag);
        VuMeterView.ApplyPalette(p);
        Vu.InvalidateVisual();
        _discordState = string.Empty; // gumb za Discord dobiva novu boju na sljedećem otkucaju
    }

    /// <summary>Dijagnostika: boja LCD teksta.</summary>
    internal Color LcdTextColor => ((SolidColorBrush)Resources["LcdText"]).Color;

    // --- V4-B: bot za Discord ----------------------------------------------------------------------------------------

    private string _discordState = string.Empty;

    internal void OnDiscord(object sender, RoutedEventArgs e) => _host.ShowDiscord(this);

    /// <summary>Oznaka bota: zelena dok šalje glazbu u kanal, inače obična; u opisu je kanal i što se šalje.</summary>
    private void TickDiscord()
    {
        JebaitedPlayer.Services.Discord.DiscordBot? bot = _host.DiscordBot;
        bool inChannel = bot?.State == JebaitedPlayer.Services.Discord.DiscordBotState.InChannel;
        bool sending = inChannel && bot!.Decision == JebaitedPlayer.Services.Discord.DiscordAudioDecision.Send;
        string state = inChannel ? $"{sending}|{bot!.Decision}|{bot.Channel?.ChannelName}" : "off";
        if (state == _discordState)
        {
            return;
        }

        _discordState = state;
        if (sending)
        {
            DiscordButton.Foreground = (Brush)FindResource("LcdText");
        }
        else
        {
            DiscordButton.ClearValue(ForegroundProperty);
        }

        DiscordButton.ToolTip = inChannel
            ? Loc.T("mini.discordInChannel", bot!.Channel?.ChannelName ?? string.Empty) + "\n" + DiscordWindow.DecisionText(bot.Decision)
            : Loc.T("mini.discord");
    }

    internal bool DiscordButtonIsLit => DiscordButton.ReadLocalValue(ForegroundProperty) != DependencyProperty.UnsetValue;

    /// <summary>Klik na oznaku izvora: Radio, Spotify, YouTube Music ili Discord Bot (ciklusi 5 i 6, V4-B).</summary>
    internal void OnSourceClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ContextMenu menu = BuildSourceMenu();
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    /// <summary>Izbornik izvora; zasebna metoda da ga test može pregledati.</summary>
    internal ContextMenu BuildSourceMenu()
    {
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var radio = new MenuItem { Header = Loc.T("menu.radio") };
        radio.Click += (_, _) => _host.ShowRadio(this);
        var spotify = new MenuItem { Header = Loc.T("menu.spotify") };
        spotify.Click += (_, _) => _host.ShowSpotify(this);
        var youTube = new MenuItem { Header = Loc.T("menu.youtube") };
        youTube.Click += (_, _) => _host.ShowYouTube(this);
        var discord = new MenuItem { Header = Loc.T("menu.discord") };
        discord.Click += (_, _) => _host.ShowDiscord(this);
        menu.Items.Add(radio);
        menu.Items.Add(spotify);
        menu.Items.Add(youTube);
        menu.Items.Add(discord);
        return menu;
    }
}
