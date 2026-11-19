using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JebaitedPlayer.Diagnostics;
using JebaitedPlayer.Infrastructure;
using Microsoft.Win32;

namespace JebaitedPlayer.Views;

/// <summary>
/// Diagnostics / Logs: lokalni pregled dnevnika playera (sažetak, događaji s filtrima, najčešći problemi, sesije) i izvoz u ZIP.
/// Čita samo lokalne datoteke u mapi logova; ništa ne šalje.
/// </summary>
public partial class DiagnosticsLogWindow : Window
{
    private const int MaxRows = 2000;

    private static readonly Brush Normal = new SolidColorBrush(Color.FromRgb(0xB8, 0xC0, 0xCC));
    private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xE8, 0xB3, 0x3C));
    private static readonly Brush Err = new SolidColorBrush(Color.FromRgb(0xF0, 0x6A, 0x6A));
    private static readonly Brush Crit = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x6E));

    private readonly string _root = AppPaths.LogsDir;
    private LogSummary _summary = new();
    private List<LogEntry> _entries = [];
    private bool _ready;
    private int _loadId;

    public DiagnosticsLogWindow()
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        MaxHeight = Math.Max(460, SystemParameters.WorkArea.Height - 24);
        Height = Math.Min(Height, MaxHeight);
        Rows.Items.Add(new LogRow(Loc.T("logs.loading"), Normal, string.Empty));
        Loaded += async (_, _) => await LoadAsync();
        this.OnLanguageChanged(() =>
        {
            RenderSummary();
            Refresh();
        });
    }

    private sealed record LogRow(string Text, Brush Brush, string Detail);

    private async Task LoadAsync()
    {
        string root = _root;
        _summary = await Task.Run(() => LogReader.Summarize(root, 30));
        _ready = false;
        FillFilters();
        _ready = true;
        RenderSummary();
        await ReloadEntriesAsync();
    }

    private void FillFilters()
    {
        DateFilter.Items.Clear();
        DateFilter.Items.Add(Loc.T("logs.filter.all"));
        foreach (DateTime d in LogReader.AvailableDays(_root).Take(60))
        {
            DateFilter.Items.Add(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        DateFilter.SelectedIndex = 0;

        SessionFilter.Items.Clear();
        SessionFilter.Items.Add(Loc.T("logs.filter.all"));
        foreach (SessionInfo s in _summary.Sessions.Take(100))
        {
            SessionFilter.Items.Add(s.Id);
        }

        SessionFilter.SelectedIndex = 0;

        LevelFilter.Items.Clear();
        LevelFilter.Items.Add(Loc.T("logs.filter.all"));
        foreach (string l in new[] { "INFO", "WARNING", "ERROR", "CRITICAL" })
        {
            LevelFilter.Items.Add(l);
        }

        LevelFilter.SelectedIndex = 0;

        CategoryFilter.Items.Clear();
        CategoryFilter.Items.Add(Loc.T("logs.filter.all"));
        foreach (string c in new[] { LogCat.App, LogCat.Usage, LogCat.Playback, LogCat.Audio, LogCat.Video, LogCat.Subtitle, LogCat.Ui, LogCat.Stability, LogCat.Update, LogCat.Logger })
        {
            CategoryFilter.Items.Add(c);
        }

        CategoryFilter.SelectedIndex = 0;
    }

    private void RenderSummary()
    {
        LogSummary s = _summary;
        string usage = $"{(int)s.TotalUsage.TotalHours} h {s.TotalUsage.Minutes:00} min";
        string cleanup = s.LastCleanup is { } c ? c.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : Loc.T("logs.summary.never");
        SummaryText.Text = string.Join("\n",
            Loc.T("logs.summary.usage", usage),
            Loc.T("logs.summary.counts", s.Launches, s.SuccessfulPlays, s.Errors, s.Warnings, s.Crashes),
            Loc.T("logs.summary.disk", FormatBytes(s.DiskBytes), cleanup));
    }

    private static string FormatBytes(long b) =>
        b >= 1 << 20 ? $"{b / (1024.0 * 1024):0.0} MB" : $"{Math.Max(b, 0) / 1024.0:0} KB";

    private async Task ReloadEntriesAsync()
    {
        int id = ++_loadId;
        DateTime from;
        DateTime to;
        if (DateFilter.SelectedIndex > 0 && DateTime.TryParseExact((string)DateFilter.SelectedItem, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime one))
        {
            from = to = one;
        }
        else
        {
            to = DateTime.Today;
            from = to.AddDays(-29);
        }

        string root = _root;
        List<LogEntry> list = await Task.Run(() => LogReader.Read(root, from, to).ToList());
        if (id != _loadId)
        {
            return;
        }

        _entries = list;
        Refresh();
    }

    private void Refresh()
    {
        if (!_ready)
        {
            return;
        }

        Rows.Items.Clear();
        DetailText.Text = string.Empty;
        bool events = ViewEvents.IsChecked == true;
        FilterPanel.IsEnabled = events;

        if (ViewSessions.IsChecked == true)
        {
            foreach (SessionInfo s in _summary.Sessions)
            {
                string state = s.Crashed ? Loc.T("logs.session.crashed") : s.Clean ? Loc.T("logs.session.clean") : Loc.T("logs.session.open");
                Rows.Items.Add(new LogRow(
                    Loc.T("logs.session.row", s.Start.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        $"{(int)s.Duration.TotalMinutes} min", s.Plays, s.Errors, s.Warnings, state),
                    s.Crashed || s.Errors > 0 ? Err : s.Warnings > 0 ? Warn : Normal, s.Id));
            }
        }
        else if (ViewProblems.IsChecked == true)
        {
            foreach (ProblemGroup g in _summary.TopProblems)
            {
                Rows.Items.Add(new LogRow(
                    Loc.T("logs.problem.row", g.Count, g.Type, g.Category, g.Last.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                    BrushFor(g.Level), g.Type));
            }
        }
        else
        {
            string? session = SessionFilter.SelectedIndex > 0 ? (string?)SessionFilter.SelectedItem : null;
            string? level = LevelFilter.SelectedIndex > 0 ? (string?)LevelFilter.SelectedItem : null;
            string? cat = CategoryFilter.SelectedIndex > 0 ? (string?)CategoryFilter.SelectedItem : null;
            IEnumerable<LogEntry> q = _entries
                .Where(e => (session is null || e.SessionId == session) && (level is null || e.LevelText == level)
                            && (cat is null || e.Category == cat) && e.Level != LogLevel.Debug)
                .OrderByDescending(e => e.Time).ThenByDescending(e => e.Seq)
                .Take(MaxRows);
            foreach (LogEntry e in q)
            {
                string detail = $"{e.TimeText} {e.LevelText} {e.Category}/{e.Type} [{e.Component}]\n{e.Message}"
                                + (e.Data.Length > 0 ? "\n" + e.Data : string.Empty)
                                + (e.Exception is { Length: > 0 } ex ? "\n" + ex : string.Empty)
                                + $"\nsession={e.SessionId} seq={e.Seq}";
                Rows.Items.Add(new LogRow($"{e.TimeText[11..]} {e.LevelText,-8} {e.Type}  {e.Message}", BrushFor(e.Level), detail));
            }
        }

        if (Rows.Items.Count == 0)
        {
            Rows.Items.Add(new LogRow(Loc.T("logs.noData"), Normal, string.Empty));
        }
    }

    private static Brush BrushFor(LogLevel l) => l switch
    {
        LogLevel.Critical => Crit,
        LogLevel.Error => Err,
        LogLevel.Warning => Warn,
        _ => Normal,
    };

    private void OnRowSelected(object sender, SelectionChangedEventArgs e) =>
        DetailText.Text = Rows.SelectedItem is LogRow r ? r.Detail : string.Empty;

    private async void OnDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready)
        {
            await ReloadEntriesAsync();
        }
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void OnViewChanged(object sender, RoutedEventArgs e) => Refresh();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_root);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_root}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Mapa logova nije otvorena: {ex.Message}");
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        bool anonymize = AnonymizeBox.IsChecked == true;
        DateTime to = DateTime.Today;
        DateTime from = DateFilter.SelectedIndex > 0 && DateTime.TryParseExact((string)DateFilter.SelectedItem, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime one) ? (to = one) : to.AddDays(-13);
        string root = _root;
        ExportPlan plan = await Task.Run(() => LogExporter.Plan(root, from, to, anonymize));
        if (plan.Items.Count == 0)
        {
            MessageBox.Show(this, Loc.T("logs.noData"), "jebaited_player", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string list = string.Join("\n", plan.Items.GroupBy(i => i.Day).Take(14)
            .Select(g => $"{g.Key:yyyy-MM-dd}: {string.Join(", ", g.Select(i => i.FileName))}"));
        string text = Loc.T("logs.exportConfirm", plan.Items.Count, plan.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            plan.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), FormatBytes(plan.TotalBytes),
            anonymize ? Loc.T("logs.exportAnon") : Loc.T("logs.exportNames"), list);
        if (MessageBox.Show(this, text, "jebaited_player", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "ZIP (*.zip)|*.zip",
            FileName = $"jebaited_player_logs_{DateTime.Now:yyyyMMdd_HHmm}.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            string target = dialog.FileName;
            int n = await Task.Run(() => LogExporter.Export(root, plan, target, AppPaths.Version));
            DiagLog.Info(LogCat.Usage, "logs.export", nameof(DiagnosticsLogWindow), "Logs exported to a local ZIP",
                ("files", n), ("anonymized", anonymize));
            MessageBox.Show(this, Loc.T("logs.exportDone", n, Path.GetFileName(target)), "jebaited_player", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Warn($"Izvoz logova nije uspio: {ex.Message}");
            MessageBox.Show(this, Loc.T("logs.exportFailed", ex.Message), "jebaited_player", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
