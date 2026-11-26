using System.Windows;
using System.Windows.Controls;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Iptv;

namespace JebaitedPlayer.Views;

/// <summary>
/// Postavke → IPTV: sve postavke IPTV-a na jednom mjestu (prekidač modula, izvori, prikaz popisa, izgled).
/// Spremaju se odmah u iptv.json; ako je IPTV panel otvoren, odmah ih učita (<see cref="IptvPanel.ReloadSettings"/>), pa ih panel
/// kasnije ne prepiše starim vrijednostima. Isključen modul nema stavku u izborniku i ne pokreće nikakve zadatke.
/// </summary>
public partial class PlayerSettingsWindow
{
    private static readonly ChannelSort[] IptvSorts =
        [ChannelSort.Original, ChannelSort.NameAsc, ChannelSort.NameDesc, ChannelSort.Manual, ChannelSort.Category, ChannelSort.FavoritesFirst];

    private bool _iptvLoading;

    private void LoadIptv()
    {
        _iptvLoading = true;
        try
        {
            IptvSettings s = new IptvStore(AppPaths.DataDir).LoadSettings();
            IptvEnableBox.IsChecked = s.Enabled;
            IptvCompactBox.IsChecked = s.CompactRows;
            IptvResumeBox.IsChecked = s.ResumeLastChannel;

            IptvSortButton.Content = IptvPanel.SortLabel(s.DefaultSort);
            IptvFontButton.Content = string.IsNullOrWhiteSpace(s.FontFamily) ? Loc.T("iptv.default") : s.FontFamily;
            IptvSizeButton.Content = Loc.T(IptvPanel.Sizes.OrderBy(z => Math.Abs(z.Scale - s.TextScale)).First().Key);
            IptvAccentButton.Content = ColorName(IptvPanel.Accents, s.AccentColor);
            IptvBackgroundButton.Content = ColorName(IptvPanel.Backgrounds, s.BackgroundColor);
            BuildIptvSources(s);
        }
        finally
        {
            _iptvLoading = false;
        }
    }

    private static string ColorName((string Key, string Hex)[] colors, string? hex) =>
        colors.Where(c => Same(c.Hex, hex)).Select(c => Loc.T(c.Key)).FirstOrDefault() ?? Loc.T("iptv.default");

    private static bool Same(string? a, string? b) => string.IsNullOrEmpty(a) ? string.IsNullOrEmpty(b) : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void ChooseIptv(Action<IptvSettings> change)
    {
        ChangeIptv(change);
        LoadIptv();
    }

    private static IptvSettings IptvNow() => new IptvStore(AppPaths.DataDir).LoadSettings();

    private void OnIptvSortClick(object sender, RoutedEventArgs e) =>
        ShowChoices(IptvSortButton, IptvSorts.Select(x => (IptvPanel.SortLabel(x), IptvNow().DefaultSort == x, (Action)(() => ChooseIptv(s => s.DefaultSort = x)))));

    private void OnIptvFontClick(object sender, RoutedEventArgs e)
    {
        string? current = IptvNow().FontFamily;
        var choices = new List<(string, bool, Action)>
        {
            (Loc.T("iptv.default"), string.IsNullOrWhiteSpace(current), () => ChooseIptv(s => s.FontFamily = null)),
        };
        choices.AddRange(IptvPanel.Fonts.Select(f => (f, current == f, (Action)(() => ChooseIptv(s => s.FontFamily = f)))));
        ShowChoices(IptvFontButton, choices);
    }

    private void OnIptvSizeClick(object sender, RoutedEventArgs e)
    {
        double current = IptvNow().TextScale;
        ShowChoices(IptvSizeButton, IptvPanel.Sizes.Select(z => (Loc.T(z.Key), Math.Abs(z.Scale - current) < 0.001, (Action)(() => ChooseIptv(s => s.TextScale = z.Scale)))));
    }

    private void OnIptvAccentClick(object sender, RoutedEventArgs e) =>
        ShowChoices(IptvAccentButton, ColorChoices(IptvPanel.Accents, IptvNow().AccentColor, (s, hex) => s.AccentColor = hex));

    private void OnIptvBackgroundClick(object sender, RoutedEventArgs e) =>
        ShowChoices(IptvBackgroundButton, ColorChoices(IptvPanel.Backgrounds, IptvNow().BackgroundColor, (s, hex) => s.BackgroundColor = hex));

    private IEnumerable<(string, bool, Action)> ColorChoices((string Key, string Hex)[] colors, string? current, Action<IptvSettings, string?> set)
    {
        yield return (Loc.T("iptv.default"), string.IsNullOrEmpty(current), () => ChooseIptv(s => set(s, null)));
        foreach ((string key, string hex) in colors)
        {
            string h = hex;
            yield return (Loc.T(key), Same(current, h), () => ChooseIptv(s => set(s, h)));
        }
    }

    private void BuildIptvSources(IptvSettings s)
    {
        IptvSourcesHost.Children.Clear();
        IptvNoSourcesText.Visibility = s.Sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (IptvSourceConfig source in s.Sources)
        {
            string id = source.Id;
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var remove = new Button { Content = Loc.T("iptv.remove"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(12, 0, 0, 0) };
            remove.SetResourceReference(StyleProperty, "SecondaryButton");
            remove.Click += (_, _) => RemoveIptvSource(id);
            DockPanel.SetDock(remove, Dock.Right);
            var radio = new RadioButton
            {
                GroupName = "IptvDefaultSource",
                Content = source.Name,
                ToolTip = Loc.T("iptv.set.defaultSource"),
                IsChecked = s.DefaultSourceId == id,
                VerticalAlignment = VerticalAlignment.Center,
            };
            radio.Checked += (_, _) =>
            {
                if (!_iptvLoading)
                {
                    ChangeIptv(x => x.DefaultSourceId = id);
                }
            };
            row.Children.Add(remove);
            row.Children.Add(radio);
            IptvSourcesHost.Children.Add(row);
        }
    }

    private void RemoveIptvSource(string id)
    {
        if (MessageBox.Show(this, Loc.T("iptv.confirmRemove"), Loc.T("iptv.title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        new IptvStore(AppPaths.DataDir).DeleteUser(id);
        ChangeIptv(x =>
        {
            x.Sources.RemoveAll(src => src.Id == id);
            if (x.DefaultSourceId == id)
            {
                x.DefaultSourceId = null;
            }

            if (x.LastSourceId == id)
            {
                x.LastSourceId = null;
            }
        });
        LoadIptv();
    }

    /// <summary>Spremi promjenu u iptv.json i javi je otvorenom IPTV panelu.</summary>
    private static void ChangeIptv(Action<IptvSettings> change)
    {
        var store = new IptvStore(AppPaths.DataDir);
        IptvSettings s = store.LoadSettings();
        change(s);
        store.SaveSettings(s);
        if (Application.Current.MainWindow is MainWindow { IptvPanelRef: { } panel })
        {
            panel.ReloadSettings();
        }
    }

    private void OnIptvEnableChanged(object sender, RoutedEventArgs e)
    {
        if (_iptvLoading)
        {
            return;
        }

        bool on = IptvEnableBox.IsChecked == true;
        ChangeIptv(s => s.Enabled = on);
        if (!on && Application.Current.MainWindow is MainWindow { IptvPanelRef: { } _ } m0)
        {
            m0.HideIptv();
        }
    }

    private void OnIptvOpen(object sender, RoutedEventArgs e)
    {
        if (IptvEnableBox.IsChecked == true && Application.Current.MainWindow is MainWindow m)
        {
            m.ShowIptv(this);
        }
    }

    private void OnIptvListChanged(object sender, RoutedEventArgs e)
    {
        if (_iptvLoading)
        {
            return;
        }

        bool compact = IptvCompactBox.IsChecked == true, resume = IptvResumeBox.IsChecked == true;
        ChangeIptv(s =>
        {
            s.CompactRows = compact;
            s.ResumeLastChannel = resume;
        });
    }

    private void OnIptvResetAppearance(object sender, RoutedEventArgs e)
    {
        ChangeIptv(s =>
        {
            s.FontFamily = null;
            s.TextScale = 1.0;
            s.AccentColor = null;
            s.BackgroundColor = null;
        });
        LoadIptv();
    }
}
