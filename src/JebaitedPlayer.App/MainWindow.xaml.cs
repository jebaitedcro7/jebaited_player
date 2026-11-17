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
    private const double DefaultSubtitleMarginY = 22;
    private static readonly TimeSpan HideControlsAfter = TimeSpan.FromSeconds(2.5);

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
    private MpvPlayer? _player;
    private nint _hwnd;
    private DateTime _lastActivity = DateTime.UtcNow;
    private bool _overlayVisible;
    private bool _contextMenuOpen;
    private bool _closing;
    private bool _closeConfirmed;

    private WindowState _restoreState = WindowState.Normal;
    private WindowStyle _restoreStyle = WindowStyle.SingleBorderWindow;
    private ResizeMode _restoreResize = ResizeMode.CanResize;

    public MainWindow(AppSettings settings, StartupOptions options)
    {
        _settings = settings;
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
        Video.VideoMouseWheel += (_, delta) =>
        {
            _vm.ChangeVolume(delta > 0 ? 5 : -5);
            RegisterActivity();
        };
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
        PreviewMouseWheel += OnPreviewMouseWheel;
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
        _uiTimer.Stop();
        _vm.Library.SaveCurrentPosition();
        if (!_options.IsDiagnostics)
        {
            SaveSettings();
        }

        _overlay?.Hide();
        _cards?.Hide();
        Hide();

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

        _store?.Dispose();
        ApplyUpdateOnExit();
        _closeConfirmed = true;
        _overlay?.Close();
        _cards?.Close();
        Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _overlay = new ControlsOverlay(_vm) { Owner = this };
        _overlay.UserActivity += (_, _) => RegisterActivity();
        _overlay.FilesDropped += (_, files) => OpenFiles(files);
        _overlay.SubtitleMenuRequested += (_, button) => ShowSubtitleMenu(button);
        _overlay.AudioMenuRequested += (_, button) => ShowItemsMenu(button, AddAudioItems);
        _overlay.SpeedMenuRequested += (_, button) => ShowItemsMenu(button, AddSpeedItems);
        _overlay.PlaylistToggleRequested += (_, _) => SetPlaylistPanel(PlaylistPanel.Visibility != Visibility.Visible);
        _ = _overlay.Handle;
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
            player.LogMessage += Log.Mpv;
            player.Initialize();
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
        _player = player;
        initWatch.Stop();
        _diag?.OnPlayerCreated(initWatch.Elapsed.TotalMilliseconds);
        Log.Info($"mpv pokrenut za {initWatch.ElapsedMilliseconds} ms (client API {MpvPlayer.ClientApiVersion})");
        return player;
    }

    private void OpenFiles(IEnumerable<string> paths)
    {
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
            Log.Error("Pokretanje mpv-a", ex);
            MessageBox.Show(this, Loc.T("error.engineStart", ex.Message),
                "jebaited_player", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _diag?.OnLoadRequested();
        _ = OpenLibraryAsync(files, folders);
        RegisterActivity();
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

    private void ShowOpenDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("home.openFile"),
            Multiselect = true,
            Filter = MediaFormats.DialogFilter,
        };

        if (dialog.ShowDialog(this) == true)
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
        bool video = _vm.HasMedia && !_vm.IsAudioOnly;
        Video.Visibility = video ? Visibility.Visible : Visibility.Hidden;
        AudioPanel.Visibility = _vm.HasMedia && _vm.IsAudioOnly ? Visibility.Visible : Visibility.Collapsed;
        WelcomePanel.Visibility = _vm.HasMedia ? Visibility.Collapsed : Visibility.Visible;
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

        bool show = _vm.HasMedia && IsVisible && WindowState != WindowState.Minimized &&
                    (_options.DiagShowControls || _vm.IsAudioOnly || _vm.IsPaused || _vm.IsScrubbing ||
                     _contextMenuOpen || _overlay.IsMouseOver ||
                     DateTime.UtcNow - _lastActivity < HideControlsAfter);

        if (show != _overlayVisible)
        {
            _overlayVisible = show;
            if (show)
            {
                UpdateOverlayBounds();
                _overlay.Show();
            }
            else
            {
                _overlay.Hide();
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

    /// <summary>Svi tipkovnički prečaci. Dijagnostika ih poziva izravno (isti put kao prava tipkovnica).</summary>
    internal bool HandleShortcut(Key key, ModifierKeys mods)
    {
        bool handled = true;
        bool ctrlOrAlt = (mods & (ModifierKeys.Control | ModifierKeys.Alt)) != 0;

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

        switch (key)
        {
            case Key.O when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                ShowOpenFolderDialog();
                break;
            case Key.O when mods == ModifierKeys.Control:
                ShowOpenDialog();
                break;
            case Key.L when mods == ModifierKeys.None && library.HasPlaylist:
                SetPlaylistPanel(PlaylistPanel.Visibility != Visibility.Visible);
                break;
            case Key.T when mods == ModifierKeys.Control:
                ShowLoadSubtitlesDialog();
                break;
            case Key.F when mods == ModifierKeys.Control:
                ShowSearchWindow();
                break;
            case Key.F1:
                ShowShortcuts();
                break;
            case Key.F or Key.F11 when mods == ModifierKeys.None:
            case Key.Enter when mods == ModifierKeys.Alt:
                SetFullscreen(!_vm.IsFullscreen);
                break;
            case Key.Escape when _vm.IsFullscreen:
                SetFullscreen(false);
                break;

            // M6: omiljeni, snimka zaslona, audio traka, omjer slike, brzina reprodukcije.
            case Key.D when mods == ModifierKeys.Control:
                library.ToggleFavorite();
                break;
            case Key.S when mods is ModifierKeys.None or ModifierKeys.Shift:
                _ = _vm.Extras.TakeScreenshotAsync(withSubtitles: mods == ModifierKeys.None);
                break;
            case Key.B when mods == ModifierKeys.None:
                _vm.Extras.CycleAudio();
                break;
            case Key.O when mods == ModifierKeys.None && _vm.HasMedia:
                _vm.Extras.CycleAspect();
                break;
            case Key.OemPlus or Key.Add when !ctrlOrAlt && _vm.HasMedia:
                _vm.Extras.StepSpeed(+1);
                break;
            case Key.OemMinus or Key.Subtract when !ctrlOrAlt && _vm.HasMedia:
                _vm.Extras.StepSpeed(-1);
                break;
            case Key.Back when mods == ModifierKeys.None && _vm.HasMedia:
                _vm.Extras.SetSpeed(1);
                break;
            default:
                handled = HandlePlaybackKey(key, mods);
                break;
        }

        if (handled)
        {
            RegisterActivity();
        }

        return handled;
    }

    private bool HandlePlaybackKey(Key key, ModifierKeys mods)
    {
        if (!_vm.HasMedia)
        {
            return false;
        }

        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        double step = ctrl ? 10 : shift ? 1000 : 100;
        SubtitleController subs = _vm.Subtitles;
        switch (key)
        {
            case Key.G:
                subs.AdjustDelay(-step);
                return true;
            case Key.H:
                subs.AdjustDelay(step);
                return true;
            case Key.Z:
                subs.MarkAudio();
                return true;
            case Key.X:
                subs.MarkSubtitle();
                return true;
            case Key.A when mods == ModifierKeys.None:
                _autoSync.SyncSelected();
                return true;
            case Key.V:
                subs.ToggleVisibility();
                return true;
            case Key.J:
                subs.CycleTrack();
                return true;
            case Key.Back when ctrl:
                subs.ResetSync();
                return true;
            case Key.Space or Key.K or Key.MediaPlayPause:
                _vm.TogglePause();
                return true;
            case Key.Left:
                _vm.SeekRelative(ctrl ? -30 : -5);
                return true;
            case Key.Right:
                _vm.SeekRelative(ctrl ? 30 : 5);
                return true;
            case Key.Up:
                _vm.ChangeVolume(5);
                return true;
            case Key.Down:
                _vm.ChangeVolume(-5);
                return true;
            case Key.M:
                _vm.ToggleMute();
                return true;
            case Key.OemComma:
                _vm.FrameStep(back: true);
                return true;
            case Key.OemPeriod:
                _vm.FrameStep(back: false);
                return true;
            case Key.N or Key.MediaNextTrack:
                _vm.Library.PlayNext();
                return true;
            case Key.P or Key.MediaPreviousTrack:
                _vm.Library.PlayPrevious();
                return true;
            case Key.Home:
                _vm.SeekAbsolute(0);
                return true;
            default:
                return false;
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Iznad popisa epizoda kotačić pomiče popis, a ne glasnoću.
        if (_vm.HasMedia && !PlaylistPanel.IsMouseOver)
        {
            _vm.ChangeVolume(e.Delta > 0 ? 5 : -5);
            RegisterActivity();
            e.Handled = true;
        }
    }

    private void ShowShortcuts()
    {
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
