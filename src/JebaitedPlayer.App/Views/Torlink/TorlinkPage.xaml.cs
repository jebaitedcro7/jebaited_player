#if TORLINK
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;
using JebaitedPlayer.Torlink;

namespace JebaitedPlayer.Views.Torlink;

/// <summary>
/// Zasebna Torlink stranica: pretraga (8 izvora odjednom, status po izvoru), rezultati (sortiranje, filter), Download Manager
/// (Aktivna / Red / Završena / Neuspjela; pauza, nastavak, otkaži, otvori mapu, reproduciraj). Sve brojke dolaze iz Torlinkovog stvarnog
/// stanja (API i datoteke stanja); preuzeto i ETA su izračunati iz njih i označeni s "~".
/// </summary>
public partial class TorlinkPage : UserControl
{
    private static readonly string[] TabKeys = ["active", "queued", "completed", "failed"];

    private readonly TorlinkService _svc;
    private readonly Action _goHome;
    private readonly Action _openSettings;
    private readonly Action<string> _play;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, DownloadRow> _rows = new();
    private string _category = "all";
    private string _sortKey = "seed";
    private bool _sortDescending = true;
    private string _tab = "active";
    private SearchResultSet? _set;
    private CancellationTokenSource? _searchCts;
    private bool _searching;
    private bool _open;

    internal TorlinkPage(TorlinkService svc, Action goHome, Action openSettings, Action<string> play)
    {
        _svc = svc;
        _goHome = goHome;
        _openSettings = openSettings;
        _play = play;
        InitializeComponent();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) => _ = RefreshAsync(), Dispatcher);
        _svc.Host.Changed += () => Dispatcher.BeginInvoke(ApplyAll);
        Loc.LanguageChanged += () => Dispatcher.BeginInvoke(ApplyAll);
        PreviewKeyDown += OnPreviewKeyDown;
        BuildTabs();
        BuildResultHeader();
    }

    private TorlinkHost Host => _svc.Host;

    // ---- pregled za testove ----
    internal int ResultCountForTests => ResultRows.Children.Count;
    internal int DownloadRowCountForTests => DownloadRows.Children.Count;
    internal string SearchTextForTests { get => SearchBox.Text; set => SearchBox.Text = value; }
    internal string MessageForTests => MessageText.Visibility == Visibility.Visible ? MessageText.Text : string.Empty;
    internal string BannerForTests => BannerBox.Visibility == Visibility.Visible ? BannerText.Text : string.Empty;
    internal string SummaryForTests => SummaryText.Text;
    internal IReadOnlyList<string> SourceChipTextsForTests => SourceChips.Children.OfType<Border>().Select(b => (b.Child as TextBlock)?.Text ?? string.Empty).ToList();
    internal Task SearchForTestsAsync() => RunAsync();
    internal Task RefreshForTestsAsync() => RefreshAsync();
    internal void SelectTabForTests(string tab) => SelectTab(tab);
    internal IReadOnlyList<DownloadRow> RowsForTests => _rows.Values.ToList();
    internal Button HomeButtonForTests => HomeButton;
    internal Task AddFirstResultForTestsAsync() => _set is { Results.Count: > 0 } s ? AddResultAsync(Sorted(s).First()) : Task.CompletedTask;

    // ---- otvaranje i zatvaranje ----

    internal void Open(string? tab)
    {
        _open = true;
        Host.PageOpened();
        if (tab is not null && TabKeys.Contains(tab))
        {
            _tab = tab;
        }

        ApplyAll();
        _timer.Start();
        _ = StartAndRefreshAsync();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => SearchBox.Focus());
    }

    internal void Close()
    {
        _open = false;
        _timer.Stop();
        _searchCts?.Cancel();
        Host.PageClosed();
    }

    private async Task StartAndRefreshAsync()
    {
        try
        {
            await Host.EnsureRunningAsync();
            await Host.RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Torlink: pokretanje", ex);
        }

        ApplyAll();
    }

    private async Task RefreshAsync()
    {
        if (!_open)
        {
            return;
        }

        try
        {
            if (Host.Status is TorlinkStatus.Ready && Host.Enabled)
            {
                await Host.EnsureRunningAsync();
            }

            await Host.RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Torlink: osvježavanje stranice", ex);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) || e.Key == Key.BrowserBack)
        {
            _goHome();
            e.Handled = true;
        }
    }

    private void OnHomeClick(object sender, RoutedEventArgs e) => _goHome();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _openSettings();

    // ---- primjena stanja na sučelje ----

    private void ApplyAll()
    {
        if (!_open)
        {
            return;
        }

        ApplyBanner();
        RenderSourceChips();
        ApplySnapshot();
    }

    private void ApplyBanner()
    {
        TorlinkStatus s = Host.Status;
        string? text = null, button = null;
        if (s == TorlinkStatus.NotInstalled)
        {
            (text, button) = (Loc.T("torlink.banner.notInstalled"), Loc.T("torlink.banner.install"));
        }
        else if (s == TorlinkStatus.Failed)
        {
            (text, button) = (Loc.T("torlink.banner.failed"), Loc.T("torlink.banner.retry"));
        }
        else if (Host.Supervisor.LastError == "restarted" && s == TorlinkStatus.Running)
        {
            text = Loc.T("torlink.banner.restarted");
        }

        BannerBox.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        BannerText.Text = text ?? string.Empty;
        BannerButton.Visibility = button is null ? Visibility.Collapsed : Visibility.Visible;
        BannerButton.Content = button ?? string.Empty;
    }

    private async void OnBannerClick(object sender, RoutedEventArgs e)
    {
        if (Host.Status == TorlinkStatus.NotInstalled)
        {
            _openSettings();
            return;
        }

        BannerButton.IsEnabled = false;
        try
        {
            await Host.Supervisor.RetryAsync(_svc.Settings.ToOptions());
        }
        finally
        {
            BannerButton.IsEnabled = true;
            ApplyAll();
        }
    }

    // ---- pretraga ----

    private void OnCategoryChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && tag != _category)
        {
            _category = tag;
            if (_set is not null && !string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                _ = RunAsync();
            }
        }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => RenderResults();

    private void OnSearchClick(object sender, RoutedEventArgs e) => _ = RunAsync();

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = RunAsync();
            e.Handled = true;
        }
    }

    private async Task RunAsync()
    {
        string text = TorlinkInput.Clean(SearchBox.Text);
        InputKind kind = TorlinkInput.Classify(text);
        switch (kind)
        {
            case InputKind.Empty:
                ShowMessage(Loc.T("torlink.searchHint"), error: false);
                return;
            case InputKind.Magnet:
            case InputKind.InfoHash:
            case InputKind.TorrentFile:
                ShowAddResult(await Host.AddInputAsync(text), null);
                if (kind != InputKind.TorrentFile)
                {
                    SearchBox.Clear();
                }

                SelectTab("active");
                return;
        }

        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _searching = true;
        ShowMessage(null, error: false);
        RenderSourceChips();
        SearchButton.IsEnabled = false;
        try
        {
            string? cat = _category == "all" ? null : _category;
            SearchResultSet set = await Host.SearchAsync(text, cat, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _set = set;
            if (set.AllFailed)
            {
                ShowMessage(Loc.T("torlink.allSourcesFailed"), error: true);
            }
        }
        catch (TorlinkSearchException ex)
        {
            ShowMessage(Loc.T(ex.Failure switch
            {
                SearchFailure.Timeout => "torlink.searchFailed.timeout",
                SearchFailure.BadOutput => "torlink.searchFailed.output",
                _ => "torlink.searchFailed.runtime",
            }), error: true);
            _set = null;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (_searchCts == cts)
            {
                _searching = false;
                SearchButton.IsEnabled = true;
            }
        }

        RenderSourceChips();
        RenderResults();
    }

    private void RenderSourceChips()
    {
        SourceChips.Children.Clear();
        if (_searching)
        {
            SourceChips.Children.Add(Chip(Loc.T("torlink.searching"), Brushes.Goldenrod));
            return;
        }

        if (_set is null)
        {
            return;
        }

        foreach (IGrouping<string, SourceStatus> g in _set.Sources.GroupBy(s => s.Label))
        {
            bool ok = g.Any(s => s.Ok);
            string text = ok
                ? Loc.T("torlink.sourceOk", g.Key, g.Sum(s => s.Count))
                : Loc.T("torlink.sourceFailed", g.Key);
            SourceChips.Children.Add(Chip(text, ok ? Brush("AccentBrush") : Brush("ErrorBrush")));
        }
    }

    private Border Chip(string text, Brush dot)
    {
        var tb = new TextBlock { Text = text, FontSize = 12, Foreground = Brush("TextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = dot, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(tb);
        return new Border { Child = panel, BorderThickness = new Thickness(1), BorderBrush = Brush("MenuBorderBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(4, 0, 0, 0), ToolTip = text }
            .WithChild(panel);
    }

    private Brush Brush(string key) => (Brush)(TryFindResource(key) ?? Brushes.Gray);

    private void BuildResultHeader()
    {
        ResultHeader.ColumnDefinitions.Clear();
        ResultHeader.Children.Clear();
        foreach (double w in new[] { -1, 84, 64, 64, 96, 112 })
        {
            ResultHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        }

        void Header(int col, string key, string? sort)
        {
            string arrow = sort is not null && sort == _sortKey ? (_sortDescending ? " ▾" : " ▴") : string.Empty;
            var b = new Button { Content = Loc.T(key) + arrow, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = sort is not null };
            b.SetResourceReference(StyleProperty, "LinkButton");
            if (sort is not null)
            {
                b.Click += (_, _) =>
                {
                    _sortDescending = _sortKey == sort ? !_sortDescending : sort != "name";
                    _sortKey = sort;
                    BuildResultHeader();
                    RenderResults();
                };
            }

            Grid.SetColumn(b, col);
            ResultHeader.Children.Add(b);
        }

        Header(0, "torlink.col.name", "name");
        Header(1, "torlink.col.size", "size");
        Header(2, "torlink.col.seed", "seed");
        Header(3, "torlink.col.leech", "leech");
        Header(4, "torlink.col.source", "source");
    }

    private IEnumerable<TorrentResult> Sorted(SearchResultSet set)
    {
        IEnumerable<TorrentResult> r = set.Results;
        if (LiveOnlyBox.IsChecked == true)
        {
            r = r.Where(x => x.Seeders > 0 || !SourceNames.ReportsHealth(x.Source));
        }

        Func<TorrentResult, object> key = _sortKey switch
        {
            "name" => x => x.Name.ToLowerInvariant(),
            "size" => x => x.SizeBytes,
            "leech" => x => x.Leechers,
            "source" => x => SourceNames.Label(x.Source),
            _ => x => x.Seeders,
        };
        return _sortDescending ? r.OrderByDescending(key) : r.OrderBy(key);
    }

    private void RenderResults()
    {
        ResultRows.Children.Clear();
        if (_set is null)
        {
            ResultHeader.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Collapsed;
            return;
        }

        List<TorrentResult> rows = Sorted(_set).Take(200).ToList();
        ResultHeader.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = rows.Count == 0 && !_set.AllFailed ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Loc.T("torlink.noResults");
        foreach (TorrentResult r in rows)
        {
            ResultRows.Children.Add(BuildResultRow(r));
        }
    }

    private UIElement BuildResultRow(TorrentResult r)
    {
        var g = new Grid { Margin = new Thickness(0) };
        foreach (double w in new[] { -1, 84, 64, 64, 96, 112 })
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        }

        TextBlock Cell(int col, string text, Brush? fg = null, string? tip = null)
        {
            var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = tip };
            if (fg is not null)
            {
                t.Foreground = fg;
            }

            Grid.SetColumn(t, col);
            g.Children.Add(t);
            return t;
        }

        TextBlock name = Cell(0, r.Name, tip: r.Name);
        name.Margin = new Thickness(0, 6, 8, 6);
        Cell(1, DownloadSnapshot.FormatSize(r.SizeBytes), Brush("TextSecondaryBrush"));
        bool health = SourceNames.ReportsHealth(r.Source);
        Cell(2, health || r.Seeders > 0 ? r.Seeders.ToString(CultureInfo.CurrentCulture) : "—", Brush("AccentBrush"),
            health ? null : Loc.T("torlink.unhealthy"));
        Cell(3, health || r.Leechers > 0 ? r.Leechers.ToString(CultureInfo.CurrentCulture) : "—", Brush("TextSecondaryBrush"));
        Cell(4, SourceNames.Label(r.Source), Brush("TextSecondaryBrush"));

        bool inList = Host.Snapshot.Contains(r.InfoHash);
        var btn = new Button { Content = Loc.T(inList ? "torlink.inList" : "torlink.download"), FontSize = 12, Padding = new Thickness(10, 3, 10, 3), IsEnabled = !inList, HorizontalAlignment = HorizontalAlignment.Right };
        btn.SetResourceReference(StyleProperty, inList ? "SecondaryButton" : "AccentButton");
        btn.Click += async (_, _) => await AddResultAsync(r);
        Grid.SetColumn(btn, 5);
        g.Children.Add(btn);

        var border = new Border { Child = g, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 1, 10, 1), Background = Brushes.Transparent };
        border.MouseEnter += (_, _) => border.Background = Brush("ButtonHoverBrush");
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        return border;
    }

    private async Task AddResultAsync(TorrentResult r)
    {
        AddResult res = await Host.AddResultAsync(r);
        ShowAddResult(res, r);
        if (res is AddResult.Added or AddResult.Duplicate or AddResult.AlreadyDownloaded)
        {
            SelectTab(res == AddResult.AlreadyDownloaded ? "completed" : "active");
            RenderResults();
        }
    }

    private void ShowAddResult(AddResult res, TorrentResult? r)
    {
        string text = res switch
        {
            AddResult.Added => Loc.T("torlink.add.added"),
            AddResult.Duplicate => Loc.T("torlink.add.duplicate"),
            AddResult.AlreadyDownloaded => Loc.T("torlink.add.already"),
            AddResult.NoSpace when Host.LastDiskCheck is { } d => Loc.T("torlink.add.nospace", d.Root, DownloadSnapshot.FormatSize(d.NeededBytes), DownloadSnapshot.FormatSize(d.FreeBytes)),
            AddResult.Invalid => Loc.T("torlink.add.invalid"),
            AddResult.TooLarge => Loc.T("torlink.add.toolarge"),
            AddResult.NotRunning => Loc.T("torlink.add.notrunning"),
            _ => Loc.T("torlink.add.failed"),
        };
        ShowMessage(text, error: res is not (AddResult.Added or AddResult.Duplicate or AddResult.AlreadyDownloaded));
    }

    private void ShowMessage(string? text, bool error)
    {
        MessageText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Text = text ?? string.Empty;
        MessageText.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorBrush" : "TextSecondaryBrush");
    }

    private void OnAddTorrentClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("torlink.torrentFilter") + "|*.torrent", Multiselect = false };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
        {
            SearchBox.Text = dlg.FileName;
            _ = RunAsync();
        }
    }

    // ---- Download Manager ----

    private void BuildTabs()
    {
        TabBar.Children.Clear();
        foreach (string key in TabKeys)
        {
            var b = new RadioButton { GroupName = "TorlinkDlTab", Tag = key, Margin = new Thickness(0, 0, 16, 0), IsChecked = key == _tab };
            b.Checked += (_, _) =>
            {
                if (_tab != key)
                {
                    _tab = key;
                    ApplySnapshot();
                }
            };
            TabBar.Children.Add(b);
        }
    }

    private void SelectTab(string tab)
    {
        _tab = tab;
        foreach (RadioButton b in TabBar.Children.OfType<RadioButton>())
        {
            b.IsChecked = (string)b.Tag == tab;
        }

        ApplySnapshot();
    }

    private static string TabLabelKey(string tab) => tab switch
    {
        "queued" => "torlink.tab.queued",
        "completed" => "torlink.tab.completed",
        "failed" => "torlink.tab.failed",
        _ => "torlink.tab.active",
    };

    private IReadOnlyList<DownloadItem> ItemsFor(string tab, DownloadSnapshot s) => tab switch
    {
        "queued" => s.Queued,
        "completed" => s.Completed,
        "failed" => s.Failed,
        _ => s.Active,
    };

    private void ApplySnapshot()
    {
        DownloadSnapshot s = Host.Snapshot;
        foreach (RadioButton b in TabBar.Children.OfType<RadioButton>())
        {
            string key = (string)b.Tag;
            int n = ItemsFor(key, s).Count;
            b.Content = Loc.T(TabLabelKey(key)) + (n > 0 ? $" ({n})" : string.Empty);
        }

        SummaryText.Text = s.DownloadingCount > 0
            ? Loc.T("torlink.summary", s.DownloadingCount, DownloadSnapshot.FormatSpeed(s.TotalSpeed))
            : Loc.T("torlink.summaryIdle");

        IReadOnlyList<DownloadItem> items = ItemsFor(_tab, s);
        var keep = new HashSet<string>(items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
        foreach (string gone in _rows.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            DownloadRows.Children.Remove(_rows[gone].Root);
            _rows.Remove(gone);
        }

        int index = 0;
        foreach (DownloadItem item in items)
        {
            if (!_rows.TryGetValue(item.Id, out DownloadRow? row))
            {
                row = new DownloadRow(item.Id, OnRowAction, Brush);
                _rows[item.Id] = row;
                DownloadRows.Children.Insert(Math.Min(index, DownloadRows.Children.Count), row.Root);
            }

            row.Update(item);
            index++;
        }

        DownloadsEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshResultButtons();
    }

    /// <summary>Gumb "Preuzmi" postaje "U popisu" čim se stavka pojavi u preuzimanjima.</summary>
    private void RefreshResultButtons()
    {
        if (_set is null || ResultRows.Children.Count == 0)
        {
            return;
        }

        int inList = ResultRows.Children.OfType<Border>().Count(b => b.Child is Grid g && g.Children.OfType<Button>().Any(x => !x.IsEnabled));
        int expected = Sorted(_set).Take(200).Count(r => Host.Snapshot.Contains(r.InfoHash));
        if (inList != expected)
        {
            RenderResults();
        }
    }

    private async void OnRowAction(string id, string action)
    {
        DownloadItem? item = Host.Snapshot.Active.Concat(Host.Snapshot.Queued).Concat(Host.Snapshot.Completed).Concat(Host.Snapshot.Failed)
            .FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        try
        {
            switch (action)
            {
                case "pause":
                case "resume":
                case "remove":
                case "delete":
                    await Host.ControlAsync(id, action);
                    break;
                case "retry":
                    await Host.ControlAsync(id, "resume");
                    break;
                case "folder" when item is not null:
                    OpenFolder(item);
                    break;
                case "play" when item is not null:
                    PlayItem(item);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Torlink: radnja " + action, ex);
        }

        ApplySnapshot();
    }

    private static string? PathOf(DownloadItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Dir))
        {
            return null;
        }

        string byName = Path.Combine(item.Dir, item.Name);
        return File.Exists(byName) || Directory.Exists(byName) ? byName : Directory.Exists(item.Dir) ? item.Dir : null;
    }

    private static void OpenFolder(DownloadItem item)
    {
        string? p = PathOf(item);
        if (p is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", File.Exists(p) || Directory.Exists(p) && p != item.Dir ? $"/select,\"{p}\"" : $"\"{p}\"") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("Torlink: otvaranje mape", ex);
        }
    }

    private void PlayItem(DownloadItem item)
    {
        string? p = PathOf(item);
        if (p is not null)
        {
            _play(p);
        }
    }

    /// <summary>Jedan redak Download Managera; ažurira se na mjestu (bez ponovnog stvaranja), pa pokazivač i listanje ostaju.</summary>
    internal sealed class DownloadRow
    {
        private readonly TextBlock _name = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _status = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        private readonly TextBlock _line = new() { FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
        private readonly ProgressBar _bar = new() { Maximum = 100, Margin = new Thickness(0, 5, 0, 2) };
        private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal };
        private readonly Dictionary<string, Button> _btn = new();
        private readonly Func<string, Brush> _brush;

        public DownloadRow(string id, Action<string, string> act, Func<string, Brush> brush)
        {
            Id = id;
            _brush = brush;
            _bar.SetResourceReference(StyleProperty, "ThinProgress");
            _line.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            foreach ((string key, string icon, string tipKey) in new[]
            {
                ("play", "", "torlink.play"), ("pause", "", "torlink.pause"), ("resume", "", "torlink.resume"),
                ("retry", "", "torlink.retry"), ("folder", "", "torlink.openFolder"), ("remove", "", "torlink.remove"),
                ("delete", "", "torlink.cancelDelete"),
            })
            {
                var b = new Button { Content = icon, ToolTip = Loc.T(tipKey), Visibility = Visibility.Collapsed, Margin = new Thickness(2, 0, 0, 0), Tag = key };
                b.SetResourceReference(StyleProperty, "IconButton");
                b.SetResourceReference(FontFamilyProperty, "IconFont");
                b.Click += (_, _) => act(id, key);
                System.Windows.Automation.AutomationProperties.SetName(b, Loc.T(tipKey));
                _btn[key] = b;
                _buttons.Children.Add(b);
            }

            var top = new DockPanel();
            DockPanel.SetDock(_buttons, Dock.Right);
            DockPanel.SetDock(_status, Dock.Right);
            top.Children.Add(_buttons);
            top.Children.Add(_status);
            top.Children.Add(_name);
            var stack = new StackPanel();
            stack.Children.Add(top);
            stack.Children.Add(_line);
            stack.Children.Add(_bar);
            Root = new Border { Child = stack, Padding = new Thickness(8, 6, 8, 4), CornerRadius = new CornerRadius(6) };
        }

        public string Id { get; }

        public Border Root { get; }

        public string NameText => _name.Text;
        public string StatusText => _status.Text;
        public string LineText => _line.Text;
        public double BarValue => _bar.Value;
        public bool IsVisibleButton(string key) => _btn[key].Visibility == Visibility.Visible;
        public void ClickForTests(string key) => _btn[key].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public void Update(DownloadItem d)
        {
            _name.Text = d.Name;
            _name.ToolTip = d.Name;
            (string statusKey, string brushKey) = d.State switch
            {
                DownloadState.Downloading => ("torlink.state.downloading", "AccentBrush"),
                DownloadState.Paused => ("torlink.state.paused", "TextSecondaryBrush"),
                DownloadState.Failed => ("torlink.state.failed", "ErrorBrush"),
                DownloadState.Completed => ("torlink.state.completed", "TextSecondaryBrush"),
                _ => ("torlink.state.queued", "TextSecondaryBrush"),
            };
            _status.Text = Loc.T(statusKey);
            _status.Foreground = _brush(brushKey);

            _line.Text = d.State switch
            {
                DownloadState.Completed => $"{DownloadSnapshot.FormatSize(d.TotalBytes)} · {Loc.T(d.Seeding ? "torlink.seeding" : "torlink.notSharing")}",
                DownloadState.Failed => string.IsNullOrEmpty(d.Error) ? Loc.T("torlink.failedNoReason") : Loc.T("torlink.failedReason", d.Error),
                DownloadState.Queued => Loc.T("torlink.waiting"),
                _ => Loc.T("torlink.progressLine", d.Percent.ToString("0", CultureInfo.CurrentCulture),
                    "~" + DownloadSnapshot.FormatSize(d.DownloadedBytes), DownloadSnapshot.FormatSize(d.TotalBytes),
                    d.State == DownloadState.Downloading ? DownloadSnapshot.FormatSpeed(d.Speed) : "—", DownloadSnapshot.FormatEta(d.EtaSeconds)),
            };
            _bar.Value = d.Percent;
            _bar.Visibility = d.State is DownloadState.Completed ? Visibility.Collapsed : Visibility.Visible;

            bool active = d.State is DownloadState.Downloading;
            Show("pause", active);
            Show("resume", d.State is DownloadState.Paused or DownloadState.Queued);
            Show("retry", d.State == DownloadState.Failed);
            Show("play", d.State == DownloadState.Completed);
            Show("folder", d.State == DownloadState.Completed || !string.IsNullOrEmpty(d.Dir));
            Show("remove", d.State is DownloadState.Completed or DownloadState.Failed);
            Show("delete", d.State is DownloadState.Downloading or DownloadState.Paused or DownloadState.Queued);
        }

        private void Show(string key, bool on) => _btn[key].Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }
}

internal static class TorlinkUiExtensions
{
    public static Border WithChild(this Border b, UIElement child)
    {
        b.Child = child;
        return b;
    }
}
#endif
