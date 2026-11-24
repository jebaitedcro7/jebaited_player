using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Iptv;
using JebaitedPlayer.ViewModels;
using Microsoft.Win32;

namespace JebaitedPlayer.Views;

/// <summary>Jedan redak u popisu kanala.</summary>
public sealed class IptvChannelRow(IptvChannel channel, bool isFavorite) : ObservableObject
{
    private bool _isFavorite = isFavorite;
    private bool _isPlaying;
    private string _nowText = string.Empty;
    private double _progress;
    private bool _hasProgress;

    public IptvChannel Channel { get; } = channel;
    public string Name => Channel.Name;
    public string Initial => Channel.Name.Length > 0 ? Channel.Name[..1].ToUpperInvariant() : "?";

    public Uri? LogoUri { get; } = Uri.TryCreate(channel.LogoUrl, UriKind.Absolute, out Uri? u) && u.Scheme is "http" or "https" ? u : null;

    private System.Windows.Media.ImageSource? _logo;

    /// <summary>Logotip se stvara tek kad se redak prikaže (virtualizacija), mali (80 px) i bez blokiranja sučelja.</summary>
    public System.Windows.Media.ImageSource? Logo
    {
        get
        {
            if (_logo is null && LogoUri is not null)
            {
                try
                {
                    var b = new System.Windows.Media.Imaging.BitmapImage();
                    b.BeginInit();
                    b.UriSource = LogoUri;
                    b.DecodePixelWidth = 80;
                    b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnDemand;
                    b.EndInit();
                    _logo = b;
                }
                catch (Exception ex) when (ex is UriFormatException or NotSupportedException or IOException)
                {
                    _logo = null;
                }
            }

            return _logo;
        }
    }

    public bool ShowLogo => LogoUri is not null;
    public bool NoLogo => LogoUri is null;
    public string StarGlyph => _isFavorite ? "" : "";
    public bool ShowDetail => _nowText.Length > 0;

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (SetField(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(StarGlyph));
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetField(ref _isPlaying, value);
    }

    public string NowText
    {
        get => _nowText;
        private set
        {
            if (SetField(ref _nowText, value))
            {
                OnPropertyChanged(nameof(ShowDetail));
            }
        }
    }

    public double Progress
    {
        get => _progress;
        private set
        {
            if (SetField(ref _progress, value))
            {
                OnPropertyChanged(nameof(FillWidth));
                OnPropertyChanged(nameof(RestWidth));
            }
        }
    }

    /// <summary>Puna traka napretka: dva stupca u omjeru (odigrano : preostalo), bez predloška i animacija (ne treperi).</summary>
    public GridLength FillWidth => new(Math.Max(_progress, 0.0001), GridUnitType.Star);

    public GridLength RestWidth => new(Math.Max(1 - _progress, 0.0001), GridUnitType.Star);

    public bool HasProgress
    {
        get => _hasProgress;
        private set => SetField(ref _hasProgress, value);
    }

    public void UpdateEpg(EpgIndex epg, DateTimeOffset now)
    {
        EpgProgram? p = epg.Now(Channel.EpgId, now);
        if (p is null)
        {
            NowText = string.Empty;
            HasProgress = false;
            return;
        }

        NowText = $"{p.Start.ToLocalTime():HH:mm}  {p.Title}";
        Progress = p.ProgressAt(now);
        HasProgress = true;
    }
}

internal sealed record IptvCategoryRow(string Key, string Name, int Count, int Kind, ImageSource? Flag = null, string? Emoji = null, bool Hidden = false);

/// <summary>
/// IPTV: izvori (Xtream, M3U adresa ili datoteka), kategorije, pretraga, favoriti, sortiranje, EPG traka. Kanal se pušta u glavnom playeru
/// (jedna instanca mpv-a). Vjerodajnice su zaštićene DPAPI-jem i nikad se ne prikazuju ni ne zapisuju u dnevnik.
/// </summary>
public partial class IptvPanel : UserControl
{
    private const double PxPerMinute = 4;
    private static readonly HttpClient Http = CreateHttp();

    private readonly IptvStore _store;
    private readonly Action<string, string> _play;
    private readonly Func<RadioPlayback> _playback;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, IptvChannelRow> _rows = [];
    private readonly DispatcherTimer _searchDebounce;
    private IptvSettings _settings;
    private IptvSourceConfig? _source;
    private IptvSession? _session;
    private IptvCatalog? _catalog;
    private IptvUserData _user = new();
    private CancellationTokenSource? _connectCts;
    private IReadOnlyList<IptvChannel> _visible = [];
    private IptvChannelRow? _playing;
    private IptvChannelRow? _previous;
    private string? _playingUrl;
    private long _playStartedAt;
    private int _dayOffset;
    private int _tick;
    private bool _showHidden;
    private IptvSourceKind _formKind;
    private IptvSourceConfig? _editing;
    private CancellationTokenSource? _testCts;

    internal IptvPanel(IptvStore store, Action<string, string> play, Func<RadioPlayback> playback)
    {
        _store = store;
        _play = play;
        _playback = playback;
        _settings = store.LoadSettings();
        InitializeComponent();
        CatCol.Width = new GridLength(Math.Clamp(_settings.CategoryWidth <= 0 ? 150 : _settings.CategoryWidth, 90, 360));
        ApplyAppearance();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => OnTick(), Dispatcher);
        _searchDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(180), DispatcherPriority.Background, (_, _) =>
        {
            _searchDebounce!.Stop();
            ApplyFilter();
        }, Dispatcher);
        _searchDebounce.Stop();
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _searchDebounce.Stop();
            _connectCts?.Cancel();
            _testCts?.Cancel();
        };
        Action lang = () => Dispatcher.BeginInvoke(UpdateTexts);
        Loaded += (_, _) => Loc.LanguageChanged += lang;
        Unloaded += (_, _) => Loc.LanguageChanged -= lang;
        KeyDown += OnWindowKeyDown;
        // Dok je panel skriven, nema tajmera ni mrežnog prometa (EPG osvježavanje).
        IsVisibleChanged += (_, e) =>
        {
            if (!_started)
            {
                return;
            }

            if ((bool)e.NewValue)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
        UpdateTexts();
    }

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"jebaited_player/{AppPaths.Version}");
        return c;
    }

    internal IptvSettings Settings => _settings;

    internal int VisibleCount => _visible.Count;

    internal string Status => StatusText.Text;

    internal IReadOnlyList<IptvChannel> VisibleChannels => _visible;

    internal IReadOnlyList<IptvChannelRow> Rows => ChannelList.ItemsSource as IReadOnlyList<IptvChannelRow> ?? [];

    // Dijagnostika (--diag-ui-test iptv).
    internal TextBox SearchTextBox => SearchBox;

    /// <summary>Koliko je redaka stvarno stvoreno (virtualizacija: mora biti puno manje od broja kanala).</summary>
    internal int RealizedRowCount => CountDescendants<ListBoxItem>(ChannelList);

    private static int CountDescendants<T>(DependencyObject d) where T : DependencyObject
    {
        int n = 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            DependencyObject c = VisualTreeHelper.GetChild(d, i);
            n += (c is T ? 1 : 0) + CountDescendants<T>(c);
        }

        return n;
    }

    internal int CategoryCount => (CategoryList.ItemsSource as IReadOnlyList<IptvCategoryRow>)?.Count ?? 0;

    internal bool IsConnected => _session is not null && _catalog is not null;

    internal int EpgProgramCount => _session?.Epg.ProgramCount ?? 0;

    internal int RowsWithEpg => _rows.Values.Count(r => r.HasProgress);

    internal int EpgCardCount => EpgCanvas.Children.OfType<Border>().Count();

    internal string InfoNowText => InfoNow.Text;

    internal string PlayStateText => PlayState.Text;

    internal bool RetryVisible => RetryButton.Visibility == Visibility.Visible;

    internal IReadOnlyList<string> FavoriteIds => _user.Favorites;

    internal void SetSortForTests(ChannelSort sort)
    {
        _settings.DefaultSort = sort;
        SaveSettings();
        UpdateTexts();
        ApplyFilter();
    }

    internal void SelectCategoryForTests(string name)
    {
        if (CategoryList.ItemsSource is IReadOnlyList<IptvCategoryRow> l && l.FirstOrDefault(c => c.Name == name) is { } c)
        {
            CategoryList.SelectedItem = c;
        }
    }

    internal int SelectedCategoryKind => (CategoryList.SelectedItem as IptvCategoryRow)?.Kind ?? -1;

    internal void SelectCategoryKindForTests(int kind)
    {
        if (CategoryList.ItemsSource is IReadOnlyList<IptvCategoryRow> l && l.FirstOrDefault(c => c.Kind == kind) is { } c)
        {
            CategoryList.SelectedItem = c;
        }
    }

    internal void SetAppearanceForTests(string? font, double scale, string? accent, string? bg)
    {
        _settings.FontFamily = font;
        _settings.TextScale = scale;
        _settings.AccentColor = accent;
        _settings.BackgroundColor = bg;
        ApplyAppearance();
    }

    internal bool HasBrushOverride(string key) => Resources.Contains(key);

    internal double CategoryColumnWidth => CatCol.ActualWidth;

    internal void ResizeCategoriesForTests(double width)
    {
        CatCol.Width = new GridLength(width);
        _settings.CategoryWidth = width;
        SaveSettings();
    }

    /// <summary>Središte razdjelnika kategorije | kanali i hvatišta na rubu, u zaslonskim pikselima (za test pravim mišem).</summary>
    internal Point CategorySplitterScreenPoint => CatSplitter.PointToScreen(new Point(CatSplitter.ActualWidth / 2, CatSplitter.ActualHeight / 2));

    internal Point EdgeGripScreenPoint => EdgeGrip.PointToScreen(new Point(EdgeGrip.ActualWidth / 2, EdgeGrip.ActualHeight / 2));

    internal void DragEdgeForTests(double dx)
    {
        double width = Math.Clamp(ActualWidth + 1 + dx, 380, 1000);
        PanelWidthRequested?.Invoke(width);
        _settings.PanelWidth = width;
        SaveSettings();
    }

    internal void ResizePanelForTests(double width)
    {
        PanelWidthRequested?.Invoke(width);
        _settings.PanelWidth = width;
        SaveSettings();
    }

    internal int FlaggedCategoryCount => (CategoryList.ItemsSource as IReadOnlyList<IptvCategoryRow>)?.Count(c => c.Flag is not null) ?? 0;

    internal IReadOnlyList<string> CategoryNames => (CategoryList.ItemsSource as IReadOnlyList<IptvCategoryRow>)?.Where(c => c.Kind == 3).Select(c => c.Name).ToList() ?? [];

    internal void SetCategoryHiddenForTests(string name, bool hide) => SetCategoryHidden(name, hide);

    internal void ToggleFavoriteForTests(IptvChannelRow row) => ToggleFavorite(row);

    internal void SelectForTests(IptvChannelRow row) => ChannelList.SelectedItem = row;

    internal void MoveForTests(IptvChannelRow row, int delta) => MoveRow(row, delta);

    // --- Pokretanje i povezivanje --------------------------------------------------------------------------------

    private bool _started;

    /// <summary>Širina cijelog panela koju korisnik povlačenjem traži od domaćina (glavni prozor mijenja širinu stupca).</summary>
    internal event Action<double>? PanelWidthRequested;

    internal double SavedPanelWidth => Math.Clamp(_settings.PanelWidth <= 0 ? 520 : _settings.PanelWidth, 380, 1000);

    // Povlačenje ruba: širina = početna širina + pomak miša od početka (mjereno prema prozoru, koji miruje).
    // Ne računa se iz „prethodne širine + promjene” jer se hvatište pomiče zajedno s rubom pa bi se kasnilo i trzalo.
    private double _edgeStartWidth, _edgeStartX;
    private bool _edgeDragging;
    private double _edgeLastRequested;

    private double WindowX(MouseEventArgs e) => e.GetPosition(Window.GetWindow(this)).X;

    /// <summary>Povlačenje ruba počelo/završilo: domaćin tada „zamrzne” područje videa (bez ponovnog slaganja svake sličice).</summary>
    internal event Action? PanelDragStarted;

    internal event Action? PanelDragCompleted;

    private void OnEdgeDown(object sender, MouseButtonEventArgs e)
    {
        PanelDragStarted?.Invoke();
        _edgeStartWidth = ActualWidth + 1;
        _edgeStartX = WindowX(e);
        _edgeDragging = true;
        EdgeGrip.CaptureMouse();
        e.Handled = true;
    }

    private void OnEdgeMove(object sender, MouseEventArgs e)
    {
        if (!_edgeDragging)
        {
            return;
        }

        double width = Math.Clamp(_edgeStartWidth + (WindowX(e) - _edgeStartX), 380, 1000);
        if (Math.Abs(width - _edgeLastRequested) >= 1)
        {
            _edgeLastRequested = width;
            PanelWidthRequested?.Invoke(width);
        }
    }

    private void OnEdgeUp(object sender, MouseButtonEventArgs e)
    {
        if (!_edgeDragging)
        {
            return;
        }

        _edgeDragging = false;
        EdgeGrip.ReleaseMouseCapture();
        PanelDragCompleted?.Invoke();
        _settings.PanelWidth = Math.Round(_edgeLastRequested > 0 ? _edgeLastRequested : ActualWidth + 1);
        SaveSettings();
        e.Handled = true;
    }

    private void OnCategorySplitterDone(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _settings.CategoryWidth = Math.Round(CatCol.ActualWidth);
        SaveSettings();
    }

    /// <summary>Traži od domaćina da sakrije panel (uključeno i kad se modul isključi).</summary>
    internal event Action? CloseRequested;

    /// <summary>Prvi put kad se panel prikaže: učitaj izvor (jednom).</summary>
    internal async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (!_settings.Enabled)
        {
            CloseRequested?.Invoke();
            return;
        }

        _timer.Start();
        if (_settings.Sources.Count == 0)
        {
            ShowEmpty(Loc.T("iptv.noSource"));
            UpdateTexts();
            return;
        }

        IptvSourceConfig src = _settings.Sources.FirstOrDefault(s => s.Id == _settings.DefaultSourceId)
            ?? _settings.Sources.FirstOrDefault(s => s.Id == _settings.LastSourceId) ?? _settings.Sources[0];
        await ConnectAsync(src);
    }

    internal async Task ConnectAsync(IptvSourceConfig src)
    {
        _connectCts?.Cancel();
        var cts = new CancellationTokenSource();
        _connectCts = cts;
        _source = src;
        _settings.LastSourceId = src.Id;
        SaveSettings();
        _catalog = null;
        _session = null;
        _rows.Clear();
        _visible = [];
        ChannelList.ItemsSource = null;
        CategoryList.ItemsSource = null;
        ClearInfo();
        UpdateTexts();
        ShowEmpty(Loc.T("iptv.connecting"));
        StatusText.Text = Loc.T("iptv.connecting");

        var session = new IptvSession(Http, src, Secret.Unprotect(src.PasswordProtected), Secret.Unprotect(src.UrlProtected));
        try
        {
            (IptvCatalog cat, XtreamAccount? acc) = await session.ConnectAsync(cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _session = session;
            _catalog = cat;
            _user = _store.LoadUser(src.Id);
            Log.Info($"IPTV: povezano, kanala {cat.Channels.Count}, šifrirano: {session.IsEncrypted}.");
            string warn = session.IsEncrypted ? string.Empty : Loc.T("iptv.unencrypted");
            string exp = acc?.Expires is { } e ? $" · {e.ToLocalTime():d}" : string.Empty;
            StatusText.Text = $"{Loc.T("iptv.loaded", cat.Channels.Count)}{exp}{(warn.Length > 0 ? " · " + warn : string.Empty)}";
            StatusText.Foreground = warn.Length > 0 ? (Brush)FindResource("ErrorBrush") : (Brush)FindResource("TextSecondaryBrush");
            RebuildCategories();
            if (_settings.ResumeLastChannel && _settings.LastChannelId is { } last)
            {
                IptvChannel? c = cat.Channels.FirstOrDefault(x => x.Id == last);
                if (c is not null)
                {
                    SelectChannel(c);
                }
            }

            _ = LoadGuideAsync(session, cts);
        }
        catch (IptvException ex) when (ex.Kind != IptvErrorKind.Cancelled)
        {
            string msg = ErrorText(ex.Kind);
            Log.Warn($"IPTV: povezivanje nije uspjelo ({ex.Kind}).");
            ShowEmpty(msg);
            StatusText.Text = msg;
            StatusText.Foreground = (Brush)FindResource("ErrorBrush");
        }
        catch (IptvException)
        {
        }
    }

    private async Task LoadGuideAsync(IptvSession session, CancellationTokenSource cts)
    {
        if (_catalog is null || session.XmltvUrl is null)
        {
            return;
        }

        try
        {
            int n = await session.LoadEpgAsync(_catalog.Channels, DateTimeOffset.UtcNow, cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(session, _session))
            {
                return;
            }

            RefreshEpgViews(rebuildStrip: true);
            if (n > 0)
            {
                StatusText.Text += " · " + Loc.T("iptv.guideLoaded", n);
            }
        }
        catch (IptvException ex) when (ex.Kind != IptvErrorKind.Cancelled)
        {
            Log.Warn($"IPTV: vodič nije učitan ({ex.Kind}).");
        }
    }

    private static string ErrorText(IptvErrorKind kind) => kind switch
    {
        IptvErrorKind.Unreachable => Loc.T("iptv.err.Unreachable"),
        IptvErrorKind.Timeout => Loc.T("iptv.err.Timeout"),
        IptvErrorKind.BadCredentials => Loc.T("iptv.err.BadCredentials"),
        IptvErrorKind.Expired => Loc.T("iptv.err.Expired"),
        IptvErrorKind.InvalidResponse => Loc.T("iptv.err.InvalidResponse"),
        _ => Loc.T("iptv.err.Http"),
    };

    private void ShowEmpty(string text)
    {
        EmptyText.Text = text;
        EmptyText.Visibility = Visibility.Visible;
    }

    private void UpdateTexts()
    {
        SourceButton.Content = _source?.Name ?? Loc.T("iptv.add");
        SortButton.Content = SortLabel(_settings.DefaultSort);
        DayButton.Content = _dayOffset == 0 ? Loc.T("iptv.today") : DayStart().ToString("ddd d.M.");
        MenuButton.ToolTip = Loc.T("iptv.title");
        StatusText.Foreground = (Brush)FindResource("TextSecondaryBrush");
    }

    private static string SortLabel(ChannelSort s) => s switch
    {
        ChannelSort.NameAsc => Loc.T("iptv.sortAz"),
        ChannelSort.NameDesc => Loc.T("iptv.sortZa"),
        ChannelSort.Manual => Loc.T("iptv.sortManual"),
        ChannelSort.Category => Loc.T("iptv.sortCategory"),
        ChannelSort.FavoritesFirst => Loc.T("iptv.sortFavFirst"),
        _ => Loc.T("iptv.sortOriginal"),
    };

    private void SaveSettings()
    {
        try
        {
            _store.SaveSettings(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("IPTV: postavke nije moguće spremiti (disk).");
        }
    }

    private void SaveUser()
    {
        if (_source is null)
        {
            return;
        }

        try
        {
            _store.SaveUser(_source.Id, _user);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("IPTV: favoriti nisu spremljeni (disk).");
        }
    }

    // --- Kategorije i popis kanala -------------------------------------------------------------------------------

    private void RebuildCategories(bool reapply = true)
    {
        if (_catalog is null)
        {
            return;
        }

        // Odabir se pamti po vrsti i ključu (Svi / Favoriti / Nedavno / grupa), da se pri promjeni favorita ne vraća na „Svi kanali”.
        IptvCategoryRow? current = CategoryList.SelectedItem as IptvCategoryRow;
        int keepKind = current?.Kind ?? 3;
        string? keep = current?.Key ?? _settings.DefaultCategory;
        var list = new List<IptvCategoryRow>
        {
            new("", Loc.T("iptv.allChannels"), ChannelQuery.Apply(_catalog.Channels, _user, new ChannelFilter(IncludeHidden: _showHidden)).Count, 0, null, "📺"),
            new("★", Loc.T("iptv.favorites"), _user.Favorites.Count, 1, null, "⭐"),
            new("⧖", Loc.T("iptv.recent"), _user.Recent.Count, 2, null, "🕘"),
        };
        var hiddenSet = new HashSet<string>(_user.HiddenCategories, StringComparer.OrdinalIgnoreCase);
        foreach ((string name, int count) in ChannelQuery.Categories(_catalog.Channels, _user, _showHidden))
        {
            ImageSource? flag = FlagFactory.ForName(name);
            list.Add(new IptvCategoryRow(name, name, count, 3, flag, flag is null ? FlagFactory.EmojiFor(name) : null, hiddenSet.Contains(name)));
        }

        CategoryList.SelectionChanged -= OnCategoryChanged;
        CategoryList.ItemsSource = list;
        CategoryList.SelectedItem = list.FirstOrDefault(c => c.Kind == keepKind && (keepKind != 3 || c.Key == keep)) ?? list[0];
        CategoryList.SelectionChanged += OnCategoryChanged;
        if (reapply)
        {
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        if (_catalog is null)
        {
            return;
        }

        var cat = CategoryList.SelectedItem as IptvCategoryRow;
        var filter = new ChannelFilter(
            SearchBox.Text,
            cat is { Kind: 3 } ? cat.Key : null,
            FavoritesOnly: cat is { Kind: 1 },
            RecentOnly: cat is { Kind: 2 },
            IncludeHidden: _showHidden,
            Sort: cat is { Kind: 2 } && _settings.DefaultSort == ChannelSort.Original ? ChannelSort.Recent : _settings.DefaultSort);
        _visible = ChannelQuery.Apply(_catalog.Channels, _user, filter);
        var rows = new List<IptvChannelRow>(_visible.Count);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (IptvChannel c in _visible)
        {
            if (!_rows.TryGetValue(c.Id, out IptvChannelRow? r))
            {
                r = new IptvChannelRow(c, _user.IsFavorite(c.Id));
                if (_session is not null)
                {
                    r.UpdateEpg(_session.Epg, now);
                }

                _rows[c.Id] = r;
            }

            r.IsFavorite = _user.IsFavorite(c.Id);
            r.IsPlaying = _playing is not null && _playing.Channel.Id == c.Id;
            rows.Add(r);
        }

        ChannelList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Loc.T("iptv.noMatch");
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is IptvCategoryRow { Kind: 3 } c && _settings.DefaultCategory != c.Key)
        {
            _settings.DefaultCategory = c.Key;
            SaveSettings();
        }

        ApplyFilter();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    // --- Izbor, favoriti, puštanje -------------------------------------------------------------------------------

    private IptvChannelRow? Selected => ChannelList.SelectedItem as IptvChannelRow;

    private void SelectChannel(IptvChannel c)
    {
        if (_rows.TryGetValue(c.Id, out IptvChannelRow? r) && ChannelList.Items.Contains(r))
        {
            ChannelList.SelectedItem = r;
            ChannelList.ScrollIntoView(r);
        }
    }

    private async void OnChannelSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is not { } row)
        {
            return;
        }

        ShowInfo(row);
        RebuildEpgStrip(row.Channel);
        // Kanal bez XMLTV podataka: kratki EPG (Xtream), jedan zahtjev po odabiru.
        if (_session is { HasShortEpg: true } s && !s.Epg.HasData(row.Channel.EpgId ?? row.Channel.Id))
        {
            try
            {
                await s.LoadShortEpgAsync(row.Channel);
                if (ReferenceEquals(Selected, row))
                {
                    ShowInfo(row);
                    RebuildEpgStrip(row.Channel);
                }
            }
            catch (IptvException)
            {
            }
        }
    }

    private void OnChannelDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindAncestor<Button>(d) is not null)
        {
            return;
        }

        PlaySelected();
    }

    private void OnChannelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PlaySelected();
            e.Handled = true;
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && !SearchBox.IsKeyboardFocused && _previous is not null)
        {
            Play(_previous);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SourceForm.Visibility == Visibility.Visible)
        {
            SourceForm.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private void OnHidePanel(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnPlayClick(object sender, RoutedEventArgs e) => PlaySelected();

    private void PlaySelected()
    {
        if (Selected is { } row)
        {
            Play(row);
        }
    }

    internal void Play(IptvChannelRow row)
    {
        if (_playing is not null)
        {
            _playing.IsPlaying = false;
            if (!ReferenceEquals(_playing, row))
            {
                _previous = _playing;
            }
        }

        _playing = row;
        row.IsPlaying = true;
        _playingUrl = row.Channel.StreamUrl;
        _playStartedAt = Environment.TickCount64;
        RetryButton.Visibility = Visibility.Collapsed;
        PlayState.Text = Loc.T("iptv.connecting");
        PlayState.Foreground = (Brush)FindResource("TextSecondaryBrush");
        _user.MarkWatched(row.Channel.Id);
        SaveUser();
        _settings.LastChannelId = row.Channel.Id;
        SaveSettings();
        _play(row.Channel.StreamUrl, row.Channel.Name);
        Log.Info("IPTV: kanal pokrenut.");
    }

    private void OnStarClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is IptvChannelRow row)
        {
            ToggleFavorite(row);
        }
    }

    private void ToggleFavorite(IptvChannelRow? row)
    {
        if (row is null)
        {
            return;
        }

        row.IsFavorite = _user.ToggleFavorite(row.Channel.Id);
        SaveUser();
        bool inFavorites = CategoryList.SelectedItem is IptvCategoryRow { Kind: 1 };
        RebuildCategories(reapply: inFavorites);
    }

    private void RebuildCategoriesKeepSelection()
    {
        IptvChannelRow? sel = Selected;
        RebuildCategories();
        if (sel is not null)
        {
            SelectChannel(sel.Channel);
        }
    }

    // --- Stanje reprodukcije -------------------------------------------------------------------------------------

    private void OnTick()
    {
        if (_playing is not null && _playingUrl is not null)
        {
            RadioPlayback pb = _playback();
            if (pb.Path != _playingUrl && Environment.TickCount64 - _playStartedAt > 4000)
            {
                // Preuzeo je neki drugi izvor (datoteka, radio): ovaj kanal više ne svira.
                _playing.IsPlaying = false;
                _playing = null;
                _playingUrl = null;
                PlayState.Text = string.Empty;
            }
            else if (pb.Error.Length > 0)
            {
                PlayState.Text = Loc.T("iptv.playError");
                PlayState.Foreground = (Brush)FindResource("ErrorBrush");
                RetryButton.Visibility = Visibility.Visible;
            }
            else
            {
                PlayState.Text = pb.Loading ? Loc.T("iptv.connecting") : string.Empty;
                RetryButton.Visibility = Visibility.Collapsed;
            }
        }

        if (++_tick % 60 == 0)
        {
            RefreshEpgViews();
        }
    }

    private Rectangle? _nowLine;

    /// <summary>Osvježi retke i info; traka se ponovno crta samo kad se podaci promijene (inače treperi i skače).</summary>
    private void RefreshEpgViews(bool rebuildStrip = false)
    {
        if (_session is null)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (IptvChannelRow r in _rows.Values)
        {
            r.UpdateEpg(_session.Epg, now);
        }

        if (Selected is { } s)
        {
            ShowInfo(s);
            if (rebuildStrip)
            {
                RebuildEpgStrip(s.Channel);
            }
            else if (_nowLine is not null && _dayOffset == 0)
            {
                DateTime day = DayStart();
                Canvas.SetLeft(_nowLine, (now - new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day))).TotalMinutes * PxPerMinute);
            }
        }

        if (_session.Epg.IsStale(now))
        {
            StatusText.Text = Loc.T("iptv.epgStale");
        }
    }

    // --- Informacije i EPG traka ---------------------------------------------------------------------------------

    private void ClearInfo()
    {
        InfoName.Text = InfoGroup.Text = InfoNow.Text = InfoNowTime.Text = InfoDesc.Text = InfoNext.Text = InfoNextTime.Text = string.Empty;
        InfoNowLabel.Text = InfoNextLabel.Text = string.Empty;
        InfoProgress.Visibility = Visibility.Collapsed;
        EpgCanvas.Children.Clear();
        EpgEmpty.Text = string.Empty;
    }

    private void ShowInfo(IptvChannelRow row)
    {
        IptvChannel c = row.Channel;
        InfoName.Text = c.Name;
        InfoGroup.Text = c.Group;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        EpgProgram? cur = _session?.Epg.Now(c.EpgId ?? c.Id, now) ?? _session?.Epg.Now(c.EpgId, now);
        EpgProgram? next = _session?.Epg.Next(c.EpgId ?? c.Id, now);
        if (cur is null && next is null)
        {
            InfoNowLabel.Text = string.Empty;
            InfoNow.Text = Loc.T("iptv.noEpg");
            InfoNow.FontWeight = FontWeights.Normal;
            InfoNowTime.Text = InfoDesc.Text = InfoNext.Text = InfoNextTime.Text = InfoNextLabel.Text = string.Empty;
            InfoProgress.Visibility = Visibility.Collapsed;
            return;
        }

        InfoNow.FontWeight = FontWeights.SemiBold;
        if (cur is not null)
        {
            InfoNowLabel.Text = Loc.T("iptv.now").ToUpperInvariant();
            InfoNow.Text = cur.Title;
            InfoNowTime.Text = $"{cur.Start.ToLocalTime():HH:mm} – {cur.End.ToLocalTime():HH:mm}";
            InfoDesc.Text = cur.Description ?? string.Empty;
            double frac = cur.ProgressAt(now);
            InfoFillCol.Width = new GridLength(Math.Max(frac, 0.0001), GridUnitType.Star);
            InfoRestCol.Width = new GridLength(Math.Max(1 - frac, 0.0001), GridUnitType.Star);
            InfoProgress.Visibility = Visibility.Visible;
        }
        else
        {
            InfoNowLabel.Text = InfoNow.Text = InfoNowTime.Text = InfoDesc.Text = string.Empty;
            InfoProgress.Visibility = Visibility.Collapsed;
        }

        if (next is not null)
        {
            InfoNextLabel.Text = Loc.T("iptv.next").ToUpperInvariant();
            InfoNext.Text = next.Title;
            InfoNextTime.Text = $"{next.Start.ToLocalTime():HH:mm} – {next.End.ToLocalTime():HH:mm}";
        }
        else
        {
            InfoNextLabel.Text = InfoNext.Text = InfoNextTime.Text = string.Empty;
        }
    }

    private DateTime DayStart() => DateTime.Today.AddDays(_dayOffset);

    private void OnDayPrev(object sender, RoutedEventArgs e) => ChangeDay(_dayOffset - 1);

    private void OnDayNext(object sender, RoutedEventArgs e) => ChangeDay(_dayOffset + 1);

    private void OnDayToday(object sender, RoutedEventArgs e) => ChangeDay(0);

    private void ChangeDay(int offset)
    {
        _dayOffset = Math.Clamp(offset, -2, 3);
        UpdateTexts();
        if (Selected is { } s)
        {
            RebuildEpgStrip(s.Channel);
        }
    }

    private void RebuildEpgStrip(IptvChannel c)
    {
        EpgCanvas.Children.Clear();
        _nowLine = null;
        EpgCanvas.Width = 24 * 60 * PxPerMinute;
        DateTime day = DayStart();
        var from = new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day));
        DateTimeOffset to = from.AddHours(24);
        string? id = c.EpgId ?? c.Id;
        IReadOnlyList<EpgProgram> programs = _session?.Epg.Range(id, from, to) ?? [];
        EpgEmpty.Text = programs.Count == 0 ? Loc.T("iptv.noEpg") : string.Empty;
        for (int h = 0; h < 24; h += 2)
        {
            var label = new TextBlock { Text = $"{h:00}:00", FontSize = 10, Foreground = (Brush)FindResource("TextSecondaryBrush") };
            Canvas.SetLeft(label, h * 60 * PxPerMinute + 2);
            Canvas.SetTop(label, 0);
            EpgCanvas.Children.Add(label);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        double? liveLeft = null;
        foreach (EpgProgram p in programs)
        {
            double left = Math.Max(0, (p.Start - from).TotalMinutes * PxPerMinute);
            double right = Math.Min(24 * 60 * PxPerMinute, (p.End - from).TotalMinutes * PxPerMinute);
            bool live = p.IsLiveAt(now);
            if (live)
            {
                liveLeft = left;
            }

            var card = new Border
            {
                Width = Math.Max(24, right - left - 3),
                Height = 62,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 3, 6, 3),
                Background = (Brush)FindResource(live ? "AccentBrush" : "MenuBackgroundBrush"),
                ToolTip = $"{p.Start.ToLocalTime():HH:mm}–{p.End.ToLocalTime():HH:mm}  {p.Title}{(p.Description is null ? "" : "\n" + p.Description)}",
                Child = new StackPanel
                {
                    ClipToBounds = true,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{p.Start.ToLocalTime():HH:mm}–{p.End.ToLocalTime():HH:mm}", FontSize = 10,
                            Foreground = (Brush)FindResource(live ? "AccentForegroundBrush" : "TextSecondaryBrush"),
                        },
                        new TextBlock
                        {
                            Text = p.Title, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
                            Foreground = (Brush)FindResource(live ? "AccentForegroundBrush" : "TextPrimaryBrush"),
                        },
                    },
                },
            };
            Canvas.SetLeft(card, left);
            Canvas.SetTop(card, 18);
            EpgCanvas.Children.Add(card);
        }

        if (_dayOffset == 0)
        {
            double x = (now - from).TotalMinutes * PxPerMinute;
            var line = new Rectangle { Width = 2, Height = 84, Fill = Brushes.Crimson, IsHitTestVisible = false };
            Canvas.SetLeft(line, x);
            EpgCanvas.Children.Add(line);
            _nowLine = line;
            // Trenutni program počinje na lijevom rubu (naslov se vidi), inače oko crvene crte.
            EpgScroll.ScrollToHorizontalOffset(Math.Max(0, (liveLeft ?? x - 120) - 6));
        }
        else
        {
            EpgScroll.ScrollToHorizontalOffset(8 * 60 * PxPerMinute);
        }
    }

    // --- Izbornici -----------------------------------------------------------------------------------------------

    private void OpenMenu(FrameworkElement anchor, ContextMenu menu)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, Action action, bool check = false, bool isChecked = false, bool enabled = true)
    {
        var mi = new MenuItem { Header = header, IsCheckable = check, IsChecked = isChecked, IsEnabled = enabled };
        mi.Click += (_, _) => action();
        return mi;
    }

    private void OnSourceClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (IptvSourceConfig s in _settings.Sources)
        {
            IptvSourceConfig src = s;
            menu.Items.Add(Item(src.Name, () => _ = ConnectAsync(src), check: true, isChecked: _source?.Id == src.Id));
        }

        if (_settings.Sources.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(Item(Loc.T("iptv.add"), () => OpenForm(null)));
        OpenMenu(SourceButton, menu);
    }

    private void OnSortClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (ChannelSort s in new[] { ChannelSort.Original, ChannelSort.NameAsc, ChannelSort.NameDesc, ChannelSort.Manual, ChannelSort.Category, ChannelSort.FavoritesFirst })
        {
            ChannelSort sort = s;
            menu.Items.Add(Item(SortLabel(sort), () =>
            {
                _settings.DefaultSort = sort;
                SaveSettings();
                UpdateTexts();
                ApplyFilter();
            }, check: true, isChecked: _settings.DefaultSort == sort));
        }

        OpenMenu(SortButton, menu);
    }

    private void OnManageClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item(Loc.T("iptv.add"), () => OpenForm(null)));
        menu.Items.Add(Item(Loc.T("iptv.edit"), () => OpenForm(_source), enabled: _source is not null));
        menu.Items.Add(Item(Loc.T("iptv.remove"), RemoveSource, enabled: _source is not null));
        menu.Items.Add(Item(Loc.T("iptv.clearLogin"), ClearLogin, enabled: _source is not null));
        menu.Items.Add(new Separator());
        menu.Items.Add(AppearanceMenu());
        menu.Items.Add(Item(Loc.T("iptv.compact"), () => { _settings.CompactRows = !_settings.CompactRows; SaveSettings(); }, check: true, isChecked: _settings.CompactRows));
        menu.Items.Add(Item(Loc.T("iptv.showHidden"), () => { _showHidden = !_showHidden; RebuildCategories(); }, check: true, isChecked: _showHidden));
        menu.Items.Add(Item(Loc.T("iptv.enable"), () =>
        {
            _settings.Enabled = false;
            SaveSettings();
            CloseRequested?.Invoke();
        }, check: true, isChecked: _settings.Enabled));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("iptv.exportPrefs"), ExportPrefs, enabled: _source is not null));
        menu.Items.Add(Item(Loc.T("iptv.importPrefs"), ImportPrefs, enabled: _source is not null));
        OpenMenu(MenuButton, menu);
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_source is not null)
        {
            await ConnectAsync(_source);
        }
    }

    private async void OnRefreshGuide(object sender, RoutedEventArgs e)
    {
        if (_session is null || _connectCts is null)
        {
            return;
        }

        StatusText.Text = Loc.T("iptv.connecting");
        await LoadGuideAsync(_session, _connectCts);
    }

    private void RemoveSource()
    {
        if (_source is null || MessageBox.Show(Window.GetWindow(this), Loc.T("iptv.confirmRemove"), Loc.T("iptv.title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        string id = _source.Id;
        _settings.Sources.RemoveAll(s => s.Id == id);
        _store.DeleteUser(id);
        if (_settings.DefaultSourceId == id) { _settings.DefaultSourceId = null; }
        if (_settings.LastSourceId == id) { _settings.LastSourceId = null; }
        SaveSettings();
        _connectCts?.Cancel();
        _source = null;
        _session = null;
        _catalog = null;
        _rows.Clear();
        ChannelList.ItemsSource = null;
        CategoryList.ItemsSource = null;
        ClearInfo();
        UpdateTexts();
        StatusText.Text = string.Empty;
        if (_settings.Sources.Count > 0)
        {
            _ = ConnectAsync(_settings.Sources[0]);
        }
        else
        {
            ShowEmpty(Loc.T("iptv.noSource"));
        }
    }

    private void ClearLogin()
    {
        if (_source is null)
        {
            return;
        }

        _source.PasswordProtected = null;
        _source.UrlProtected = null;
        SaveSettings();
    }

    private void ExportPrefs()
    {
        var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "iptv-preferences.json" };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
        {
            File.WriteAllText(dlg.FileName, _store.ExportPreferences(_settings, _user));
        }
    }

    private void ImportPrefs()
    {
        var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            _store.ImportPreferences(File.ReadAllText(dlg.FileName), _settings, _user);
            SaveSettings();
            SaveUser();
            UpdateTexts();
            RebuildCategories();
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            StatusText.Text = Loc.T("iptv.err.InvalidResponse");
        }
    }

    // Kontekstni izbornik retka kanala.
    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonUp(e);
        if (e.OriginalSource is DependencyObject cd && FindAncestor<ListBoxItem>(cd) is { DataContext: IptvCategoryRow { Kind: 3 } cat })
        {
            var cmenu = new ContextMenu();
            if (cat.Hidden)
            {
                cmenu.Items.Add(Item(Loc.T("iptv.showCategory"), () => SetCategoryHidden(cat.Name, false)));
            }
            else
            {
                cmenu.Items.Add(Item(Loc.T("iptv.hideCategory"), () => SetCategoryHidden(cat.Name, true)));
            }

            cmenu.PlacementTarget = this;
            cmenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            cmenu.IsOpen = true;
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is DependencyObject d && FindAncestor<ListBoxItem>(d) is { DataContext: IptvChannelRow row })
        {
            ChannelList.SelectedItem = row;
            var menu = new ContextMenu();
            menu.Items.Add(Item(Loc.T("iptv.favorites"), () => ToggleFavorite(row), check: true, isChecked: row.IsFavorite));
            menu.Items.Add(Item(Loc.T("iptv.hide"), () =>
            {
                if (!_user.HiddenChannels.Contains(row.Channel.Id))
                {
                    _user.HiddenChannels.Add(row.Channel.Id);
                }

                SaveUser();
                RebuildCategories();
            }));
            bool manual = _settings.DefaultSort == ChannelSort.Manual;
            menu.Items.Add(Item(Loc.T("iptv.up"), () => MoveRow(row, -1), enabled: manual));
            menu.Items.Add(Item(Loc.T("iptv.down"), () => MoveRow(row, 1), enabled: manual));
            menu.PlacementTarget = this;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        }
    }

    /// <summary>Sakrij (ili ponovno prikaži) cijelu kategoriju; skrivena se vidi samo uz „Prikaži skrivene kanale”.</summary>
    private void SetCategoryHidden(string name, bool hide)
    {
        _user.HiddenCategories.RemoveAll(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (hide)
        {
            _user.HiddenCategories.Add(name);
        }

        SaveUser();
        if (hide && !_showHidden && CategoryList.SelectedItem is IptvCategoryRow { Kind: 3 } sel && sel.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            CategoryList.SelectedIndex = 0;
        }

        RebuildCategories();
    }

    private void MoveRow(IptvChannelRow row, int delta)
    {
        if (_catalog is null)
        {
            return;
        }

        // Ručni redoslijed vrijedi za cijeli popis (ne samo za prikazanu kategoriju).
        List<string> order = ChannelQuery.Apply(_catalog.Channels, _user, new ChannelFilter(IncludeHidden: true, Sort: ChannelSort.Manual)).Select(c => c.Id).ToList();
        int i = order.IndexOf(row.Channel.Id);
        if (i < 0)
        {
            return;
        }

        _user.Move(order, row.Channel.Id, i + delta);
        SaveUser();
        ApplyFilter();
        SelectChannel(row.Channel);
    }

    private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        for (DependencyObject? c = d; c is not null; c = c is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(c) : LogicalTreeHelper.GetParent(c))
        {
            if (c is T t)
            {
                return t;
            }
        }

        return null;
    }

    // --- Obrazac za vezu -----------------------------------------------------------------------------------------

    private void OpenForm(IptvSourceConfig? edit)
    {
        _editing = edit;
        FormTitle.Text = Loc.T(edit is null ? "iptv.add" : "iptv.edit");
        FName.Text = edit?.Name ?? "IPTV";
        FAddress.Text = edit?.Kind switch { IptvSourceKind.Xtream => edit.Server, IptvSourceKind.M3uFile => edit.FilePath, IptvSourceKind.M3uUrl => string.Empty, _ => string.Empty };
        FUser.Text = edit?.Username ?? string.Empty;
        FPass.Password = string.Empty;
        FPassHint.Visibility = edit is null ? Visibility.Collapsed : Visibility.Visible;
        FXmltv.Text = edit?.XmltvUrl ?? string.Empty;
        FResult.Text = string.Empty;
        SetFormKind(edit?.Kind ?? IptvSourceKind.Xtream);
        SourceForm.Visibility = Visibility.Visible;
        FAddress.Focus();
    }

    private void SetFormKind(IptvSourceKind kind)
    {
        _formKind = kind;
        FType.Content = kind switch { IptvSourceKind.M3uUrl => "M3U / M3U8 URL", IptvSourceKind.M3uFile => "M3U / M3U8 " + Loc.T("iptv.file"), _ => "Xtream Codes" };
        FAddressLabel.Text = kind switch { IptvSourceKind.M3uUrl => Loc.T("iptv.url"), IptvSourceKind.M3uFile => Loc.T("iptv.file"), _ => Loc.T("iptv.server") };
        FCredentials.Visibility = kind == IptvSourceKind.Xtream ? Visibility.Visible : Visibility.Collapsed;
        FBrowse.Visibility = kind == IptvSourceKind.M3uFile ? Visibility.Visible : Visibility.Collapsed;
        UpdateInsecureWarning();
    }

    private void UpdateInsecureWarning()
    {
        string a = FAddress.Text.Trim();
        bool http = _formKind != IptvSourceKind.M3uFile && a.Length > 0 && !a.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        FWarn.Text = http ? Loc.T("iptv.unencrypted") : string.Empty;
    }

    private void OnFormTypeClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Xtream Codes", () => SetFormKind(IptvSourceKind.Xtream)));
        menu.Items.Add(Item("M3U / M3U8 URL", () => SetFormKind(IptvSourceKind.M3uUrl)));
        menu.Items.Add(Item("M3U / M3U8 " + Loc.T("iptv.file"), () => SetFormKind(IptvSourceKind.M3uFile)));
        OpenMenu(FType, menu);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "M3U|*.m3u;*.m3u8|*.*|*.*" };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
        {
            FAddress.Text = dlg.FileName;
            UpdateInsecureWarning();
        }
    }

    private void OnAddressLostFocus(object sender, RoutedEventArgs e)
    {
        string a = FAddress.Text.Trim();
        if (a.Length == 0)
        {
            return;
        }

        DetectedSource d = IptvSourceDetector.Detect(a);
        SetFormKind(d.Kind);
        if (d.Kind == IptvSourceKind.Xtream)
        {
            FAddress.Text = d.Server ?? a;
            if (d.Username is not null) { FUser.Text = d.Username; }
            if (d.Password is not null) { FPass.Password = d.Password; }
        }

        UpdateInsecureWarning();
    }

    private IptvSourceConfig BuildFromForm()
    {
        IptvSourceConfig cfg = _editing ?? new IptvSourceConfig();
        cfg.Name = string.IsNullOrWhiteSpace(FName.Text) ? "IPTV" : FName.Text.Trim();
        cfg.Kind = _formKind;
        string a = FAddress.Text.Trim();
        cfg.XmltvUrl = string.IsNullOrWhiteSpace(FXmltv.Text) ? null : FXmltv.Text.Trim();
        switch (_formKind)
        {
            case IptvSourceKind.Xtream:
                cfg.Server = a;
                cfg.Username = FUser.Text.Trim();
                if (FPass.Password.Length > 0)
                {
                    cfg.PasswordProtected = Secret.Protect(FPass.Password);
                }

                cfg.UrlProtected = null;
                cfg.FilePath = null;
                break;
            case IptvSourceKind.M3uUrl:
                if (a.Length > 0)
                {
                    cfg.UrlProtected = Secret.Protect(a.Contains("://", StringComparison.Ordinal) ? a : "http://" + a);
                }

                cfg.Server = cfg.Username = cfg.PasswordProtected = null;
                cfg.FilePath = null;
                break;
            default:
                cfg.FilePath = a;
                cfg.Server = cfg.Username = cfg.PasswordProtected = cfg.UrlProtected = null;
                break;
        }

        return cfg;
    }

    private async void OnFormTest(object sender, RoutedEventArgs e)
    {
        // Jedan pokušaj po kliku; nema automatskog ponavljanja (da se račun ne zaključa).
        FTest.IsEnabled = false;
        FResult.Foreground = (Brush)FindResource("TextSecondaryBrush");
        FResult.Text = Loc.T("iptv.connecting");
        _testCts?.Cancel();
        var cts = new CancellationTokenSource();
        _testCts = cts;
        try
        {
            IptvSourceConfig tmp = CloneForTest();
            var s = new IptvSession(Http, tmp, FPass.Password.Length > 0 ? FPass.Password : Secret.Unprotect(_editing?.PasswordProtected),
                tmp.Kind == IptvSourceKind.M3uUrl ? FAddress.Text.Trim() : null);
            (IptvCatalog cat, _) = await s.ConnectAsync(cts.Token);
            FResult.Text = Loc.T("iptv.testOk", cat.Channels.Count);
        }
        catch (IptvException ex) when (ex.Kind != IptvErrorKind.Cancelled)
        {
            FResult.Text = ErrorText(ex.Kind);
            FResult.Foreground = (Brush)FindResource("ErrorBrush");
        }
        finally
        {
            FTest.IsEnabled = true;
        }
    }

    private IptvSourceConfig CloneForTest() => new()
    {
        Kind = _formKind,
        Server = FAddress.Text.Trim(),
        Username = FUser.Text.Trim(),
        FilePath = FAddress.Text.Trim(),
        XmltvUrl = null,
    };

    private void OnFormCancel(object sender, RoutedEventArgs e)
    {
        _testCts?.Cancel();
        SourceForm.Visibility = Visibility.Collapsed;
    }

    private async void OnFormSave(object sender, RoutedEventArgs e)
    {
        IptvSourceConfig cfg = BuildFromForm();
        if (!_settings.Sources.Any(s => s.Id == cfg.Id))
        {
            _settings.Sources.Add(cfg);
        }

        _settings.DefaultSourceId ??= cfg.Id;
        SaveSettings();
        SourceForm.Visibility = Visibility.Collapsed;
        await ConnectAsync(cfg);
    }
}
