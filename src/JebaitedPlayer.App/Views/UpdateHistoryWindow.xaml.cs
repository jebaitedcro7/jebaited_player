using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;

namespace JebaitedPlayer.Views;

/// <summary>
/// Povijest nadogradnji (UPGRADE 3): sve verzije s datumom, novim funkcionalnostima, poboljšanjima, ispravcima i
/// poznatim problemima, na jeziku sučelja. Lokalna povijest (ugrađena u aplikaciju) prikazuje se odmah i bez interneta;
/// u pozadini se s GitHuba dohvate samo novije verzije od instalirane (ako ih ima).
/// </summary>
public partial class UpdateHistoryWindow : Window
{
    private readonly string _currentVersion;
    private readonly bool _checkOnline;
    private OnlineState _online = OnlineState.Checking;
    private IReadOnlyList<OnlineRelease> _newer = [];

    public UpdateHistoryWindow(string currentVersion, bool checkOnline = true)
    {
        _currentVersion = currentVersion;
        _checkOnline = checkOnline;
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 24);
        Height = Math.Min(Height, MaxHeight);
        _online = checkOnline ? OnlineState.Checking : OnlineState.Skipped;
        Render();
        this.OnLanguageChanged(Render);
        if (checkOnline)
        {
            Loaded += async (_, _) => await CheckOnlineAsync();
        }
    }

    private enum OnlineState
    {
        Checking,
        Offline,
        UpToDate,
        Newer,
        Skipped,
    }

    /// <summary>Dijagnostika: stanje provjere na GitHubu i broj prikazanih verzija.</summary>
    internal string OnlineStatus => _online.ToString();

    internal int ShownVersions => UpdateHistory.Versions.Count;

    private async Task CheckOnlineAsync()
    {
        IReadOnlyList<OnlineRelease>? newer = await UpdateHistory.FetchNewerAsync(_currentVersion);
        _newer = newer ?? [];
        _online = newer is null ? OnlineState.Offline : newer.Count > 0 ? OnlineState.Newer : OnlineState.UpToDate;
        Render();
    }

    private void Render()
    {
        InstalledText.Text = Loc.T("history.installed", _currentVersion);
        OnlineStatusText.Text = _online switch
        {
            OnlineState.Checking => Loc.T("history.checking"),
            OnlineState.Offline => Loc.T("history.offline"),
            OnlineState.UpToDate => Loc.T("history.latest"),
            OnlineState.Newer => Loc.T("history.newerHint"),
            _ => string.Empty,
        };
        OnlineStatusText.Visibility = OnlineStatusText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        Entries.Children.Clear();
        foreach (OnlineRelease release in _newer)
        {
            Entries.Children.Add(OnlineCard(release));
        }

        Version? current = UpdateHistory.ParseVersion(_currentVersion);
        foreach (ReleaseEntry entry in UpdateHistory.Versions)
        {
            Entries.Children.Add(LocalCard(entry, entry.ParsedVersion == current));
        }
    }

    private Border LocalCard(ReleaseEntry entry, bool installed)
    {
        ReleaseNotesText notes = entry.NotesFor(Loc.Current);
        var panel = new StackPanel();
        panel.Children.Add(Header(Loc.T("history.versionHeader", entry.Version), UpdateHistory.FormatDate(entry),
            installed ? Loc.T("history.current") : null));
        if (notes.Summary.Length > 0)
        {
            panel.Children.Add(new TextBlock { Text = notes.Summary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        }

        AddSection(panel, "history.features", notes.Features);
        AddSection(panel, "history.improvements", notes.Improvements);
        AddSection(panel, "history.fixes", notes.Fixes);
        AddSection(panel, "history.known", notes.Known);
        return Card(panel, installed);
    }

    private Border OnlineCard(OnlineRelease release)
    {
        var panel = new StackPanel();
        panel.Children.Add(Header(Loc.T("history.versionHeader", release.Version),
            release.Published is { } d ? UpdateHistory.FormatDate(d.LocalDateTime) : string.Empty,
            Loc.T("history.newer")));
        string body = PlainText(release.Body);
        if (body.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = body.Length > 2500 ? body[..2500].TrimEnd() + " …" : body,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
            });
        }

        if (release.Url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            var open = new Button { Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            open.Bind(ContentProperty, "history.openGitHub");
            open.Click += (_, _) => OpenUrl(release.Url);
            panel.Children.Add(open);
        }

        return Card(panel, highlight: true);
    }

    private DockPanel Header(string title, string date, string? badge)
    {
        var header = new DockPanel { LastChildFill = false };
        var name = new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(name, Dock.Left);
        header.Children.Add(name);
        if (badge != null)
        {
            var tag = new Border
            {
                Background = (Brush)FindResource("SelectionBrush"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 1, 7, 2),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = badge, FontSize = 12, Foreground = (Brush)FindResource("AccentBrush") },
            };
            DockPanel.SetDock(tag, Dock.Left);
            header.Children.Add(tag);
        }

        var when = new TextBlock { Text = date, Foreground = (Brush)FindResource("TextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(when, Dock.Right);
        header.Children.Add(when);
        return header;
    }

    private void AddSection(StackPanel panel, string titleKey, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        panel.Children.Add(new TextBlock { Text = Loc.T(titleKey), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        foreach (string item in items)
        {
            var row = new DockPanel { Margin = new Thickness(4, 2, 0, 2) };
            var bullet = new TextBlock { Text = "•", Margin = new Thickness(0, 0, 8, 0), Foreground = (Brush)FindResource("AccentBrush") };
            DockPanel.SetDock(bullet, Dock.Left);
            row.Children.Add(bullet);
            row.Children.Add(new TextBlock { Text = item, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(row);
        }
    }

    private Border Card(UIElement content, bool highlight) => new()
    {
        Background = (Brush)FindResource(highlight ? "InputBackgroundBrush" : "PanelBackgroundBrush"),
        BorderBrush = (Brush)FindResource(highlight ? "AccentBrush" : "MenuBorderBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(16, 12, 16, 14),
        Margin = new Thickness(0, 10, 0, 0),
        Child = content,
    };

    /// <summary>Bilješke s GitHuba su Markdown; prikazuju se kao običan tekst (bez oznaka i poveznica).</summary>
    internal static string PlainText(string markdown)
    {
        string text = Regex.Replace(markdown, @"\[([^\]]+)\]\([^)]+\)", "$1");
        text = Regex.Replace(text, @"(?m)^#{1,6}\s*", string.Empty);
        text = text.Replace("**", string.Empty).Replace("`", string.Empty);
        text = Regex.Replace(text, @"(?m)^\s*[-*]\s+", "• ");
        text = Regex.Replace(text, @"(?m)^-{3,}\s*$", string.Empty);
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private void OnAllReleasesClick(object sender, RoutedEventArgs e) => OpenUrl(UpdateHistory.Repository + "/releases");

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Poveznica se nije otvorila: {ex.Message}");
        }
    }
}
