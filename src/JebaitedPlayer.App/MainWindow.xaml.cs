using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JebaitedPlayer.Diagnostics;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Library;
using JebaitedPlayer.Mpv;
using JebaitedPlayer.Services;
using JebaitedPlayer.ViewModels;
using JebaitedPlayer.Views;
using static JebaitedPlayer.Interop.NativeMethods;

namespace JebaitedPlayer;

public partial class MainWindow : Window
{
    private const double OverlayHeightDip = 116;
    private const double TopBarHeightDip = 64;
    private const double DefaultSubtitleMarginY = 22;

    /// <summary>Koliko dugo bez pomicanja miša kontrole (i Home) ostaju vidljive; iz postavki (Izgled), zadano 2,5 s.</summary>
    private TimeSpan HideControlsAfter => TimeSpan.FromSeconds(Math.Clamp(_settings.ControlsHideSeconds, 1, 30));

    private double _appliedSubtitleMargin = DefaultSubtitleMarginY;

    private readonly PlayerViewModel _vm;
    private readonly AppSettings _settings;
    private readonly StartupOptions _options;
    private readonly DispatcherTimer _uiTimer;
    private readonly DiagnosticsRunner? _diag;

    private readonly WatchStore? _store;
    private readonly OnlineSubtitleController _online;
    private readonly SpeechAnalysisService _speech;
    private readonly AutoSyncController _autoSync;
    private ControlsOverlay? _overlay;
    private TopBarOverlay? _topBar;
    private MpvPlayer? _player;
    private nint _hwnd;
    private DateTime _lastActivity = DateTime.UtcNow;
    private bool _overlayVisible;
    private bool _contextMenuOpen;
    private bool _closing;
    private PlayerEventTracker? _events;
    private HealthMonitor? _health;
    private bool _closeConfirmed;

    private WindowState _restoreState = WindowState.Normal;
    private WindowStyle _restoreStyle = WindowStyle.SingleBorderWindow;
    private ResizeMode _restoreResize = ResizeMode.CanResize;

    public MainWindow(AppSettings settings, StartupOptions options)
    {
        _settings = settings;
        KeyboardShortcuts.Settings = settings; // 2.0: F1 i početni ekran pokazuju stvarne (i promijenjene) tipke
        _options = options;

        // Dijagnostika: automatska sinkronizacija samo za ovo pokretanje (postavke se u dijagnostici ne spremaju).
        if (options.DiagAutoSync is bool autoSync)
        {
            settings.AutoSyncSubtitles = autoSync;
        }

        // Dijagnostika nikad ne dira tvoju stvarnu povijest gledanja.
        string dbPath = options.IsDiagnostics
            ? options.DiagDb ?? Path.Combine(Path.GetTempPath(), "jebaited_player_diag", Guid.NewGuid().ToString("N") + ".db")
            : AppPaths.LibraryDb;
        try
        {
            _store = new WatchStore(dbPath);
        }
        catch (Exception ex)
        {
            Log.Error("Baza povijesti nije otvorena (nastavak gledanja neće raditi)", ex);
        }

        StartDailyBackup(options);

        _vm = new PlayerViewModel(settings, _store);

        InitializeComponent();
        DataContext = _vm;
        _vm.Subtitles.Message += message => Notify(message);
        _vm.Library.Message += message => Notify(message);
        _vm.Extras.Message += message => Notify(message);
        _vm.Library.OpenRecentRequested += (_, path) => OpenFiles([path]);
        _vm.Library.PlaylistOpened += (_, _) => SetPlaylistPanel(true);
        _vm.Library.PropertyChanged += OnLibraryPropertyChanged;
        if (options.DiagUpNextSeconds is int upNext)
        {
            _vm.Library.UpNextDelaySeconds = upNext;
        }

        if (options.IsDiagnostics)
        {
            // Testovi ne pune tvoju mapu slika.
            _vm.Extras.FolderOverride = options.DiagScreenshotFolder ?? Path.Combine(Path.GetTempPath(), "jebaited_player_diag", "screenshots");
            if (options.DiagAutoNext is bool autoNext)
            {
                _vm.Library.AutoPlayNext = autoNext;
            }
        }

        _online = new OnlineSubtitleController(settings, _vm, options.IsDiagnostics && options.DiagFakeOnline is null && !options.DiagOnlineLive
                ? []
                : FakeOnlineProviders(options.DiagFakeOnline));
        _online.Message += message => Notify(message);
        _vm.Online = _online;

        // Analiza govora: dijagnostika koristi zasebnu predmemoriju (testovi ne pune tvoju).
        _speech = new SpeechAnalysisService(options.IsDiagnostics
            ? Path.Combine(Path.GetTempPath(), "jebaited_player_diag", "speech")
            : AppPaths.SpeechCacheDir);
        _autoSync = new AutoSyncController(settings, _vm, _speech);
        _autoSync.Message += message => Notify(message);
        _vm.AutoSync = _autoSync;
        _online.Verifier = _autoSync;
        _ = Task.Run(() => _speech.CleanupCache(TimeSpan.FromDays(120)));
        _vm.Subtitles.ForcedCodePageForQueued = options.DiagEncoding;
        SubtitleService.FallbackCodePage = settings.SubtitleCodePage;
        _vm.MediaLoaded += (_, _) => OnMediaLoadedForWindow();
        _vm.LoadStarting += (_, path) => OnLoadStarting(path);
        SubtitleService.CleanupCache(TimeSpan.FromDays(14));
        if (!options.IsDiagnostics && JebaitedPlayer.Online.SubtitleSearchService.PurgeCachedKeys(AppPaths.SearchCacheDir) is int purged and > 0)
        {
            Log.Info($"Iz predmemorije pretrage obrisano {purged} datoteka s API ključem (spremljene u ranijoj verziji).");
        }

        if (options.IsDiagnostics)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // 1280×720, ali uvijek unutar zaslona (i na manjim rezolucijama).
            Width = Math.Min(1280, SystemParameters.WorkArea.Width - 40);
            Height = Math.Min(720, SystemParameters.WorkArea.Height - 40);
            // Mjerimo stvarno vidljiv prozor (ne smije biti skriven iza drugih prozora).
            // (--diag-normal-z: kao u običnom radu, za provjeru redoslijeda prozora, npr. izbornika i kontrola.)
            Topmost = !Environment.GetCommandLineArgs().Contains("--diag-normal-z");
        }
        else
        {
            _vm.Volume = settings.Volume;
            _vm.IsMuted = settings.Muted;
            ApplySavedBounds();
        }

        _vm.OpenRequested += (_, _) => ShowOpenDialog();
        _vm.FullscreenToggleRequested += (_, _) => SetFullscreen(!_vm.IsFullscreen);
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        Loc.LanguageChanged += OnLanguageChanged;

        // Prekidač teme na početnom ekranu (usklađen s onim u postavkama).
        HomeThemeSwitch.IsChecked = !WindowTheme.IsDark;
        HomeThemeSwitch.Checked += (_, _) => SetTheme("light");
        HomeThemeSwitch.Unchecked += (_, _) => SetTheme("dark");
        WindowTheme.Changed += () => HomeThemeSwitch.IsChecked = !WindowTheme.IsDark;

        Video.VideoMouseMove += (_, _) => RegisterActivity();
        Video.VideoClick += (_, _) =>
        {
            RegisterActivity();
            _vm.TogglePause();
        };
        Video.VideoDoubleClick += (_, _) =>
        {
            // Prvi klik dvoklika je već pauzirao/pokrenuo video; vrati to stanje i promijeni cijeli zaslon.
            _vm.TogglePause();
            SetFullscreen(!_vm.IsFullscreen);
        };
        Video.VideoRightClick += (_, _) => ShowContextMenu();
        // 2.1.9: kotačić mijenja glasnoću bilo gdje na playeru (video, trake, Home); popisi koji se listaju kotačićem se i dalje listaju.
        Video.VideoMouseWheel += (_, delta) => ChangeVolumeByWheel(delta);
        Video.FilesDropped += (_, files) => OpenFiles(files);
        Video.FocusReceived += (_, _) => Dispatcher.BeginInvoke(RestoreKeyboardFocus);

        _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background,
            (_, _) => OnUiTick(), Dispatcher);

        if (options.IsDiagnostics)
        {
            _diag = new DiagnosticsRunner(options, _vm, Video, () => _player, Close, HandleShortcut);
            _vm.PlaybackRestarted += (_, _) => _diag.OnPlaybackRestart();
            _vm.LoadFailed += (_, _) => _diag.OnLoadFailed();
            if (options.DiagShow is { } show)
            {
                bool shown = false;
                _vm.PlaybackRestarted += async (_, _) =>
                {
                    if (shown)
                    {
                        return;
                    }

                    shown = true;
                    await Task.Delay(2500);
                    ShowForDiagnostics(show);
                };
            }
        }

        InitializeUiTests();
        Loaded += OnLoaded;
        LocationChanged += (_, _) => UpdateOverlayBounds();
        SizeChanged += (_, _) => UpdateOverlayBounds();
        StateChanged += (_, _) =>
        {
            UpdateOverlayBounds();
            UpdateOverlayVisibility();
        };
        DpiChanged += (_, _) => UpdateOverlayBounds();
        Activated += (_, _) => RestoreKeyboardFocus();
        MouseMove += (_, _) => RegisterActivity();
        PreviewKeyDown += OnPreviewKeyDown;
        InitializeUpdates();
    }

    /// <summary>
    /// Jezik je promijenjen (postavke): tekstovi vezani u XAML-u mijenjaju se sami, a ovdje se ponovno slože tekstovi
    /// koje slaže kod (povijest, popis epizoda, statusi). Jezik se pamti i opisi u Windowsima se osvježe.
    /// </summary>
    private void OnLanguageChanged()
    {
        _vm.RefreshTexts();
        _autoSync.RefreshTexts();
        _online.RefreshTexts();
        System.Windows.Data.CollectionViewSource.GetDefaultView(_vm.Library.Playlist).Refresh();
        if (!_options.IsDiagnostics)
        {
            _settings.Language = Loc.Current;
            _settings.Save();
            ShellIntegration.RefreshTextsInBackground();
        }

        Log.Info($"Jezik sučelja promijenjen: {Loc.Current}");
    }

    /// <summary>Dijagnostika i snimke zaslona: otvori zadani prozor ili izbornik (uz video ili na početnom ekranu).</summary>
    private void ShowForDiagnostics(string show)
    {
        Log.Info($"Dijagnostika: otvaram {show}");
        switch (show)
        {
            case "sync":
                ShowSyncWindow();
                break;
            case "context":
                ShowContextMenu();
                break;
            case "settings":
                ShowSubtitleSettings();
                break;
            case "menu" when _overlay != null:
                ShowSubtitleMenu(_overlay.SubtitlesButton);
                break;
            case "playlist":
                SetPlaylistPanel(true);
                break;
            case "search":
                ShowSearchWindow();
                break;
            case "sources":
                ShowSourcesWindow();
                break;
            case "player-settings":
                ShowPlayerSettings();
                break;
            case "wizard":
                ShowOnlineSetup();
                break;
            case "wizard-os":
                ShowOnlineSetup(1);
                break;
            case "context-menu":
                ShowContextMenu();
                break;
            case "language-picker":
                new LanguagePickerWindow { Owner = this }.Show();
                break;

            // UPGRADE 3: kartice postavki ("settings-audio"…), povijest nadogradnji i izbornik broja verzije.
            case not null when show.StartsWith("settings-", StringComparison.Ordinal)
                               && Enum.TryParse(show["settings-".Length..], ignoreCase: true, out SettingsTab tab):
                ShowPlayerSettings(tab);
                break;
            case "update-history":
                ShowUpdateHistory();
                break;
            case "version-menu":
                ShowVersionMenu();
                break;
        }
    }

    private void OnHomeSettingsClick(object sender, RoutedEventArgs e) => ShowPlayerSettings();

    private void SetTheme(string theme)
    {
        if (theme == WindowTheme.Current)
        {
            return;
        }

        WindowTheme.Apply(theme);
        if (!_options.IsDiagnostics)
        {
            _settings.Theme = theme;
            _settings.Save();
        }

        Log.Info($"Tema: {theme}");
    }

    private void OnWelcomeSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLogoSize();

    /// <summary>Logo se prilagođava visini prozora: manji kad je prozor nizak ili kad su prikazani popisi.</summary>
    private void UpdateLogoSize()
    {
        double h = WelcomePanel.ActualHeight;
        if (h <= 0)
        {
            return;
        }

        double logo = Math.Clamp(h * (_vm.Library.HasLibraryContent ? 0.16 : 0.24), 52, 170);
        LogoImage.Height = logo;
        LogoBlock.Height = logo * 1.3;
        LogoBlock.Width = logo * 2.1;
        AuthorName.FontSize = Math.Clamp(logo * 0.2, 17, 30);
        ProductName.Visibility = h < 430 ? Visibility.Collapsed : Visibility.Visible;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Naslovna traka u boji teme (Windows 11).
        WindowTheme.ApplyTitleBar(this);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _diag?.OnContentRendered();

        // Datoteke iz naredbenog retka otvaramo tek kad je prozor već prikazan,
        // da korisnik odmah vidi prozor, a mpv se pokreće odmah nakon toga.
        OpenStartupFiles();
        UpdateOnlineSetupButton();
        OfferOnlineSetupOnFirstRun();

        // N4: vlastita pozadina se učitava tek kad je prozor prikazan, izvan UI dretve (ne usporava pokretanje).
        _ = ApplyHomeBackgroundAsync();
        InitializeHomeInfo();
        AskErrorReportConsent();
        if (_options.DiagShow is { } show && _options.Files.Count == 0)
        {
            // Dijagnostika stalno mjeri iscrtavanje (dretva nikad nije "idle"), pa se prozor otvara nakon kratke pauze.
            _ = Task.Delay(1500).ContinueWith(_ => ShowForDiagnostics(show), TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            OpenFiles(files);
        }

        e.Handled = true;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closeConfirmed)
        {
            base.OnClosing(e);
            return;
        }

        // Gašenje mpv-a ide u pozadini dok UI dretva i dalje obrađuje poruke (vidi MpvPlayer.Dispose).
        e.Cancel = true;
        if (_closing)
        {
            return;
        }

        _closing = true;
        ReleaseInstance();
        CloseMiniPlayer();
        _uiTimer.Stop();
        _vm.Library.SaveCurrentPosition();
        if (!_options.IsDiagnostics)
        {
            SaveSettings();
        }

        _overlay?.Hide();
        _topBar?.Hide();
        _cards?.Hide();
        Hide();

        // V4-B: bot izlazi iz glasovnog kanala prije gašenja mpv-a (najviše 3 s).
        await CloseDiscordAsync();

        _events?.Detach();
        _health?.Dispose();
        _health = null;
        Log.Info("Player closing");
        MpvPlayer? player = _player;
        _player = null;
        if (player != null)
        {
            try
            {
                await Task.Run(player.Dispose);
            }
            catch (Exception ex)
            {
                Log.Error("Gašenje mpv-a", ex);
            }
        }

        _spotify?.Dispose();
        _reports?.Dispose();
        _youTube?.Dispose();
        _store?.Dispose();
        ApplyUpdateOnExit();
        _closeConfirmed = true;
        _overlay?.Close();
        _topBar?.Close();
        _cards?.Close();
        Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _health = new HealthMonitor(Dispatcher, () => _vm.HasMedia && !_vm.IsPaused);
        _health.Start();
        _overlay = new ControlsOverlay(_vm) { Owner = this };
        _overlay.UserActivity += (_, _) => RegisterActivity();
        _overlay.FilesDropped += (_, files) => OpenFiles(files);
        _overlay.SubtitleMenuRequested += (_, button) => ShowSubtitleMenu(button);
        _overlay.AudioMenuRequested += (_, button) => ShowItemsMenu(button, AddAudioItems);
        _overlay.SpeedMenuRequested += (_, button) => ShowItemsMenu(button, AddSpeedItems);
        _overlay.PlaylistToggleRequested += (_, _) => SetPlaylistPanel(PlaylistPanel.Visibility != Visibility.Visible);
        _ = _overlay.Handle;

        // Gornja traka s tipkom Home (UPGRADE 3): prazan dio radi kao video (klik, dvoklik, desni klik, kotačić).
        _topBar = new TopBarOverlay { Owner = this };
        _topBar.UserActivity += (_, _) => RegisterActivity();
        _topBar.FilesDropped += (_, files) => OpenFiles(files);
        _topBar.HomeRequested += (_, _) => GoHome();
        _topBar.BackgroundClick += (_, _) =>
        {
            RegisterActivity();
            _vm.TogglePause();
        };
        _topBar.BackgroundDoubleClick += (_, _) =>
        {
            // Prvi klik dvoklika je već pauzirao/pokrenuo video; vrati to stanje i promijeni cijeli zaslon (kao na videu).
            _vm.TogglePause();
            SetFullscreen(!_vm.IsFullscreen);
        };
        _topBar.BackgroundRightClick += (_, _) => ShowContextMenu();
        _topBar.MouseWheel += (_, delta) => ChangeVolumeByWheel(delta);
        _ = _topBar.Handle;
        InitializeHomePlayback();
        InitializeLibraryUi();

        _uiTimer.Start();
        RestoreKeyboardFocus();

        if (_options.IsDiagnostics && _options.DiagFullscreen)
        {
            SetFullscreen(true);
        }
    }

    private MpvPlayer EnsurePlayer()
    {
        if (_player != null)
        {
            return _player;
        }

        nint wid = Video.VideoHandle;
        if (wid == 0)
        {
            Video.Visibility = Visibility.Visible;
            Video.UpdateLayout();
            wid = Video.VideoHandle;
        }

        if (wid == 0)
        {
            throw new InvalidOperationException("Video prozor nije spreman.");
        }

        var initWatch = System.Diagnostics.Stopwatch.StartNew();
        var player = new MpvPlayer(SynchronizationContext.Current);
        try
        {
            player.SetOption("wid", wid.ToString(CultureInfo.InvariantCulture));
            try
            {
                Directory.CreateDirectory(AppPaths.ShaderCacheDir);
                player.TrySetOption("gpu-shader-cache-dir", AppPaths.ShaderCacheDir);
                player.TrySetOption("icc-cache-dir", AppPaths.ShaderCacheDir);
            }
            catch (IOException ex)
            {
                Log.Warn($"Cache shadera nije dostupan: {ex.Message}");
            }

            foreach (var (name, value) in MpvConfig.BaseOptions)
            {
                if (!player.TrySetOption(name, value))
                {
                    Log.Warn($"mpv opcija nije prihvaćena: {name}={value}");
                }
            }

            player.TrySetOption("volume", _vm.Volume.ToString(CultureInfo.InvariantCulture));
            player.TrySetOption("mute", _vm.IsMuted ? "yes" : "no");
            if (_options.IsDiagnostics)
            {
                player.TrySetOption("mute", "yes");
                // Datoteka se vrti u krug (stabilna mjerenja), osim kad se testira prelazak na sljedeću epizodu.
                if (_options.DiagUpNextSeconds is null)
                {
                    player.TrySetOption("loop-file", "inf");
                }
            }

            MpvConfig.ApplySubtitleOptionsBeforeInit(player, _settings);
            MpvConfig.ApplyPlaybackOptionsBeforeInit(player, _settings);
            player.LogMessage += Log.Mpv;
            player.Initialize();
            MpvConfig.CaptureDefaults(player);
            player.RequestLogMessages("warn");
            foreach (var (name, format) in MpvConfig.ObservedProperties)
            {
                player.ObserveProperty(name, format);
            }
        }
        catch
        {
            player.Dispose();
            throw;
        }

        player.VideoReconfig += (_, _) => Video.DisableVideoChildren();
        player.FileLoaded += (_, _) => Video.DisableVideoChildren();
        _vm.Attach(player);
        _events = new PlayerEventTracker(player, _vm);
        _player = player;
        initWatch.Stop();
        _diag?.OnPlayerCreated(initWatch.Elapsed.TotalMilliseconds);
        Log.Info($"mpv pokrenut za {initWatch.ElapsedMilliseconds} ms (client API {MpvPlayer.ClientApiVersion})");
        return player;
    }

    private void OpenFiles(IEnumerable<string> paths)
    {
        _userOpenPending = true;
        var all = paths.ToList();
        var folders = all.Where(Directory.Exists).ToList();
        var subtitles = all.Where(File.Exists).Where(MediaFormats.IsSubtitle).ToList();
        var files = all.Where(File.Exists).Where(p => !MediaFormats.IsSubtitle(p)).ToList();
        if (files.Count == 0 && folders.Count == 0)
        {
            if (subtitles.Count > 0)
            {
                if (_vm.HasMedia)
                {
                    _vm.Subtitles.LoadFiles(subtitles);
                }
                else
                {
                    Notify(Loc.T("notice.openVideoBeforeLoad"));
                }
            }

            return;
        }

        // Titlovi povučeni zajedno s videom učitaju se čim se video učita.
        _vm.Subtitles.QueueForNextFile(subtitles);

        try
        {
            EnsurePlayer();
        }
        catch (Exception ex)
        {
            Log.Stability.Error("Pokretanje mpv-a", ex);
            MessageBox.Show(this, Loc.T("error.engineStart", ex.Message),
                "jebaited_player", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _diag?.OnLoadRequested();
        _fullscreenOnLoad = _settings.OpenFullscreen && !_options.IsDiagnostics;
        _ = OpenLibraryAsync(files, folders);
        RegisterActivity();
        RestoreKeyboardFocus();
    }

    /// <summary>
    /// Tipka Home (UPGRADE 3): spremi poziciju, zatvori video i vrati početni ekran. Aplikacija ostaje otvorena;
    /// pri ponovnom otvaranju istog videa nudi se nastavak gledanja. Iz cijelog zaslona vraća se u prozor.
    /// </summary>
    internal void GoHome()
    {
        if (!_vm.HasMedia)
        {
            return;
        }

        _vm.RefreshPosition();
        Log.Info($"Povratak na početni ekran: {Path.GetFileName(_vm.CurrentPath)} na {_vm.Position:0.0} s od {_vm.Duration:0.0} s (pozicija se sprema).");
        _vm.Library.LeaveToHome();
        _autoSync.Reset();
        if (_vm.IsFullscreen)
        {
            SetFullscreen(false);
        }

        // 2.1.7: glazba i radio sviraju dalje (isti mpv, bez novog učitavanja); zaustavlja se samo video.
        if (HomeKeepsPlaying)
        {
            EnterHomeWhilePlaying();
            return;
        }

        _vm.Stop();
        RestoreKeyboardFocus();
    }

    private void Notify(string message)
    {
        if (_vm.HasMedia)
        {
            _vm.ShowOsd(message, 3000);
        }
        else
        {
            _vm.Notice = message;
        }
    }

    private void ShowOpenDialog(Window? owner = null)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("home.openFile"),
            Multiselect = true,
            Filter = MediaFormats.DialogFilter,
        };

        if (dialog.ShowDialog(owner ?? this) == true)
        {
            OpenFiles(dialog.FileNames);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.HasMedia):
            case nameof(PlayerViewModel.IsAudioOnly):
                UpdatePanels();
                UpdateOverlayVisibility();
                UpdateTitle();
                if (!_vm.HasMedia)
                {
                    SetPlaylistPanel(false);
                    _vm.Library.RefreshRecent();
                }

                UpdateCards();
                ApplyWindowSettings();
                break;
            case nameof(PlayerViewModel.Title):
                UpdateTitle();
                break;
            case nameof(PlayerViewModel.IsPaused):
                UpdateOverlayVisibility();
                if (_vm.IsPaused)
                {
                    _vm.Library.SaveCurrentPosition();
                }

                break;
        }
    }

    private void UpdatePanels()
    {
        if (!_vm.HasMedia)
        {
            _homeView = false;
        }

        bool home = _homeView && _vm.HasMedia;
        bool video = _vm.HasMedia && !_vm.IsAudioOnly && !home;
        Video.Visibility = video ? Visibility.Visible : Visibility.Hidden;
        AudioPanel.Visibility = _vm.HasMedia && _vm.IsAudioOnly && !home ? Visibility.Visible : Visibility.Collapsed;
        WelcomePanel.Visibility = _vm.HasMedia && !home ? Visibility.Collapsed : Visibility.Visible;
        UpdateHomePlayback();
    }

    private void UpdateTitle()
    {
        Title = _vm.HasMedia && _vm.Title.Length > 0 ? $"{_vm.Title} – jebaited_player" : "jebaited_player";
    }

    private void OnUiTick()
    {
        _vm.RefreshPosition();
        _vm.Library.OnTick();
        UpdateOverlayVisibility();
        UpdateHomePlayback();
        AudioLive.Visibility = _vm.IsLiveStream && !_vm.IsPaused && AudioPanel.IsVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RegisterActivity()
    {
        _lastActivity = DateTime.UtcNow;
        UpdateOverlayVisibility();
    }

    private void UpdateOverlayVisibility()
    {
        if (_overlay is null || _closing)
        {
            return;
        }

        bool show = _vm.HasMedia && !_homeView && IsVisible && WindowState != WindowState.Minimized &&
                    (_options.DiagShowControls || _vm.IsAudioOnly || _vm.IsPaused || _vm.IsScrubbing ||
                     _contextMenuOpen || _overlay.IsMouseOver || _topBar?.IsMouseOver == true ||
                     DateTime.UtcNow - _lastActivity < HideControlsAfter);

        if (show != _overlayVisible)
        {
            _overlayVisible = show;
            // Home (gornja traka) i donje kontrole uvijek se prikazuju i skrivaju zajedno.
            if (show)
            {
                UpdateOverlayBounds();
                _overlay.Show();
                _topBar?.Show();
            }
            else
            {
                _overlay.Hide();
                _topBar?.Hide();
            }

            ApplySubtitleMargin();
        }

        Video.CursorHidden = _vm.HasMedia && !_vm.IsAudioOnly && !show;
    }

    /// <summary>Dok su kontrole vidljive, titlovi se podignu iznad njih (inače bi ih kontrole prekrile).</summary>
    private void ApplySubtitleMargin()
    {
        if (_player is null)
        {
            return;
        }

        double margin = DefaultSubtitleMarginY;
        if (_overlayVisible && _vm.HasMedia && !_vm.IsAudioOnly && Video.ActualHeight > 100)
        {
            // sub-margin-y je u "skaliranim" pikselima (visina 720), pa visinu kontrola preračunavamo.
            margin += (OverlayHeightDip - 12) * 720.0 / Video.ActualHeight;
        }

        margin = Math.Round(margin);
        if (Math.Abs(margin - _appliedSubtitleMargin) < 1)
        {
            return;
        }

        _appliedSubtitleMargin = margin;
        _player.SetPropertyAsync("sub-margin-y", margin);
    }

    /// <summary>Postavlja prozor s kontrolama točno preko donjeg dijela videa, a kartice preko cijelog videa (fizički pikseli).</summary>
    private void UpdateOverlayBounds()
    {
        if (_overlay is null || !TryGetVideoAreaRect(out int x, out int y, out int width, out int areaHeight))
        {
            return;
        }

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        int height = Math.Min(areaHeight, (int)Math.Round(OverlayHeightDip * scale));
        SetWindowPos(_overlay.Handle, 0, x, y + areaHeight - height, width, height,
            SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER);
        if (_topBar != null)
        {
            // Gornja traka: uz gornji rub videa (Home gore lijevo), najviše trećina visine videa.
            int topHeight = Math.Max(1, Math.Min(areaHeight / 3, (int)Math.Round(TopBarHeightDip * scale)));
            SetWindowPos(_topBar.Handle, 0, x, y, width, topHeight, SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER);
        }

        if (_cards != null)
        {
            SetWindowPos(_cards.Handle, 0, x, y, width, areaHeight, SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER);
        }

        ApplySubtitleMargin();
    }

    private bool TryGetVideoAreaRect(out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;
        if (_hwnd == 0 || PresentationSource.FromVisual(VideoArea) is null || VideoArea.ActualWidth < 1)
        {
            return false;
        }

        Point topLeft = VideoArea.PointToScreen(new Point(0, 0));
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        x = (int)Math.Round(topLeft.X);
        y = (int)Math.Round(topLeft.Y);
        width = (int)Math.Round(VideoArea.ActualWidth * dpi.DpiScaleX);
        height = (int)Math.Round(VideoArea.ActualHeight * dpi.DpiScaleY);
        return width > 0 && height > 0;
    }

    private void SetFullscreen(bool fullscreen)
    {
        if (fullscreen == _vm.IsFullscreen)
        {
            return;
        }

        if (fullscreen)
        {
            _restoreState = WindowState;
            _restoreStyle = WindowStyle;
            _restoreResize = ResizeMode;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }

            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = _restoreStyle;
            ResizeMode = _restoreResize;
            WindowState = _restoreState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        }

        _vm.IsFullscreen = fullscreen;
        UpdateOverlayBounds();
        RegisterActivity();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleKey(e))
        {
            e.Handled = true;
        }
    }

    /// <summary>Svi prečaci playera. Koriste ga i dijaloški prozori (npr. G/H i Z/X u prozoru sinkronizacije).</summary>
    internal bool HandleKey(KeyEventArgs e) => HandleShortcut(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);

    /// <summary>
    /// Svi tipkovnički prečaci. 2.0: tipke dolaze iz registra <see cref="Shortcuts"/> i korisnikovih promjena (Postavke →
    /// Prečaci). Stalnu ulogu imaju kartice (nastavak gledanja, sljedeća epizoda), Esc, Alt+Enter i medijske tipke.
    /// Dijagnostika ih poziva izravno (isti put kao prava tipkovnica).
    /// </summary>
    internal bool HandleShortcut(Key key, ModifierKeys mods)
    {
        // Dok je otvorena kartica (nastavak gledanja / sljedeća epizoda), Enter i ostale tipke odgovaraju njoj.
        LibraryController library = _vm.Library;
        if (library.HasResumeOffer && key is Key.Enter or Key.Space or Key.Home or Key.Back)
        {
            if (key is Key.Home or Key.Back)
            {
                library.ResumeFromStart();
            }
            else
            {
                library.ResumeContinue();
            }

            RegisterActivity();
            return true;
        }

        if (library.HasUpNext && key is Key.Enter or Key.Escape)
        {
            if (key == Key.Enter)
            {
                library.UpNextPlayNow();
            }
            else
            {
                library.UpNextCancel();
            }

            return true;
        }

        string? action = key switch
        {
            Key.Escape when _vm.IsFullscreen => "exitFullscreen",
            Key.Enter when mods == ModifierKeys.Alt => "fullscreen",
            Key.MediaPlayPause => "playPause",
            Key.MediaNextTrack => "nextEpisode",
            Key.MediaPreviousTrack => "previousEpisode",
            _ => Shortcuts.Resolve(_settings, key, mods),
        };
        if (action is null || !RunShortcut(action))
        {
            return false;
        }

        RegisterActivity();
        return true;
    }

    /// <summary>Izvršava radnju prečaca (ID iz <see cref="Shortcuts.Actions"/>); false ako sada nema smisla (npr. nema videa).</summary>
    internal bool RunShortcut(string action)
    {
        LibraryController library = _vm.Library;
        switch (action)
        {
            case "openFile":
                ShowOpenDialog();
                return true;
            case "openFolder":
                ShowOpenFolderDialog();
                return true;
            case "loadSubtitles":
                ShowLoadSubtitlesDialog();
                return true;
            case "searchSubtitles":
                ShowSearchWindow();
                return true;
            case "help":
                ShowShortcuts();
                return true;
            case "fullscreen":
                SetFullscreen(!_vm.IsFullscreen);
                return true;
            case "exitFullscreen":
                SetFullscreen(false);
                return true;
            case "favorite":
                library.ToggleFavorite();
                return true;
            case "screenshot":
                _ = _vm.Extras.TakeScreenshotAsync(withSubtitles: true);
                return true;
            case "screenshotPictureOnly":
                _ = _vm.Extras.TakeScreenshotAsync(withSubtitles: false);
                return true;
            case "nextAudio":
                _vm.Extras.CycleAudio();
                return true;
            case "miniPlayer":
                ToggleMiniPlayer();
                return true;
            case "radio":
                ShowRadio();
                return true;
            case "spotify":
                ShowSpotify();
                return true;
            case "youTube":
                ShowYouTube();
                return true;
            case "discord":
                ShowDiscord();
                return true;
            case "settings":
                ShowPlayerSettings();
                return true;
            case "playlist":
                if (!library.HasPlaylist)
                {
                    return false;
                }

                SetPlaylistPanel(PlaylistPanel.Visibility != Visibility.Visible);
                return true;
        }

        if (!_vm.HasMedia)
        {
            // R1: razmak / medijska tipka ponovno spaja radio zaustavljen tipkom Stop.
            return action == "playPause" && _vm.ResumeStoppedStream();
        }

        int seek = Math.Clamp(_settings.SeekStepSeconds, 1, 60);
        SubtitleController subs = _vm.Subtitles;
        switch (action)
        {
            case "playPause":
                _vm.TogglePause();
                break;
            case "seekBack":
                _vm.SeekRelative(-seek);
                break;
            case "seekForward":
                _vm.SeekRelative(seek);
                break;
            case "seekBackLong":
                _vm.SeekRelative(-seek * 6);
                break;
            case "seekForwardLong":
                _vm.SeekRelative(seek * 6);
                break;
            case "seekStart":
                _vm.SeekAbsolute(0);
                break;
            case "volumeUp":
                _vm.ChangeVolume(5);
                break;
            case "volumeDown":
                _vm.ChangeVolume(-5);
                break;
            case "mute":
                _vm.ToggleMute();
                break;
            case "frameBack":
                _vm.FrameStep(back: true);
                break;
            case "frameForward":
                _vm.FrameStep(back: false);
                break;
            case "speedUp":
                _vm.Extras.StepSpeed(+1);
                break;
            case "speedDown":
                _vm.Extras.StepSpeed(-1);
                break;
            case "speedNormal":
                _vm.Extras.SetSpeed(1);
                break;
            case "aspect":
                _vm.Extras.CycleAspect();
                break;
            case "toggleSubtitles":
                subs.ToggleVisibility();
                break;
            case "nextSubtitles":
                subs.CycleTrack();
                break;
            case "subtitleEarlier":
                subs.AdjustDelay(-100);
                break;
            case "subtitleLater":
                subs.AdjustDelay(100);
                break;
            case "subtitleEarlierFine":
                subs.AdjustDelay(-10);
                break;
            case "subtitleLaterFine":
                subs.AdjustDelay(10);
                break;
            case "subtitleEarlierCoarse":
                subs.AdjustDelay(-1000);
                break;
            case "subtitleLaterCoarse":
                subs.AdjustDelay(1000);
                break;
            case "markAudio":
                subs.MarkAudio();
                break;
            case "markSubtitle":
                subs.MarkSubtitle();
                break;
            case "autoSync":
                _autoSync.SyncSelected();
                break;
            case "resetSync":
                subs.ResetSync();
                break;
            case "nextEpisode":
                library.PlayNext();
                break;
            case "previousEpisode":
                library.PlayPrevious();
                break;
            default:
                return false;
        }

        return true;
    }

    private void ShowShortcuts()
    {
        KeyboardShortcuts.Settings = _settings;
        _vm.RefreshShortcuts();
        if (_vm.HasMedia)
        {
            _vm.ShowOsd(KeyboardShortcuts.AsOsdText(), 6000);
        }
        else
        {
            _vm.ShowWelcomeShortcuts = !_vm.ShowWelcomeShortcuts;
        }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => ShowOpenDialog();

    private void RestoreKeyboardFocus()
    {
        if (_closing)
        {
            return;
        }

        if (_hwnd != 0 && IsActive)
        {
            SetFocus(_hwnd);
        }

        Keyboard.Focus(this);
    }

    private void ApplySavedBounds()
    {
        if (_settings.WindowWidth is double w && w > 300 && _settings.WindowHeight is double h && h > 200 &&
            _settings.WindowLeft is double left && _settings.WindowTop is double top)
        {
            var bounds = new Rect(left, top, w, h);
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (screen.IntersectsWith(bounds))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
                Width = w;
                Height = h;
            }
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (_settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveSettings()
    {
        Rect bounds = WindowState == WindowState.Normal && !_vm.IsFullscreen
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        _settings.Volume = _vm.Volume;
        _settings.Muted = _vm.IsMuted;
        if (!bounds.IsEmpty)
        {
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }

        _settings.WindowMaximized = _vm.IsFullscreen
            ? _restoreState == WindowState.Maximized
            : WindowState == WindowState.Maximized;
        _settings.Save();
    }
}
