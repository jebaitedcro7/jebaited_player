using System.Windows;
using System.Windows.Controls;
using JebaitedPlayer.Controls;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;

namespace JebaitedPlayer.Views;

/// <summary>
/// 2.1.7: prilagodba izgleda vremena, datuma i prognoze na početnom ekranu: font, veličina i mjesto svakog elementa, odabir
/// podataka prognoze, pregled prije spremanja i vraćanje na zadano. Promjene vrijede odmah (radna kopija postavki), a „Spremi”
/// i „Odbaci promjene” rade kao za sve ostalo.
/// </summary>
public partial class PlayerSettingsWindow
{
    private static readonly double[] SizeChoices = [12, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64, 80];

    private readonly Dictionary<string, Button> _homeLookButtons = [];
    private HomeInfoLayer? _homePreview;

    private void BuildHomeLook()
    {
        HomeLookPanel.Children.Clear();
        _homeLookButtons.Clear();
        HomeInfoSettings h = _settings.HomeInfo;

        foreach ((string key, Func<HomeInfoSettings, HomeElementStyle> style) in Elements)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(new TextBlock { Text = Loc.T("homeElement." + key), Width = 90, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(MakeChoice(key + ".font", 150, () => FontChoices(style(h))));
            row.Children.Add(MakeChoice(key + ".size", 80, () => SizeList(style(h), key)));
            row.Children.Add(MakeChoice(key + ".pos", 130, () => PositionChoices(style(h))));
            HomeLookPanel.Children.Add(row);
        }

        var details = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (string id in HomeInfoSettings.AllDetails)
        {
            var box = new CheckBox { Content = Loc.T("weatherDetail." + id), IsChecked = h.WeatherDetails.Contains(id), Margin = new Thickness(0, 2, 18, 2) };
            box.Click += (_, _) => SetHomeInfo(() =>
            {
                List<string> list = [.. h.WeatherDetails];
                if (box.IsChecked == true)
                {
                    list.Add(id);
                }
                else
                {
                    list.Remove(id);
                }

                h.WeatherDetails = HomeInfoSettings.AllDetails.Where(list.Contains).ToList();
            }, weather: false);
            details.Children.Add(box);
        }

        HomeDetailsHost.Child = details;

        _homePreview = new HomeInfoLayer { Width = 330, Height = 170, ClipToBounds = true };
        HomePreviewHost.Child = _homePreview;
        RefreshHomeLook();
    }

    private static readonly (string Key, Func<HomeInfoSettings, HomeElementStyle> Style)[] Elements =
    [
        ("time", h => h.TimeStyle),
        ("date", h => h.DateStyle),
        ("weather", h => h.WeatherStyle),
    ];

    private Button MakeChoice(string id, double width, Func<IEnumerable<(string, bool, Action)>> choices)
    {
        var button = new Button { Style = (Style)FindResource("ChoiceButton"), Width = width, MinWidth = width, Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => ShowChoices(button, choices());
        _homeLookButtons[id] = button;
        return button;
    }

    private IEnumerable<(string, bool, Action)> FontChoices(HomeElementStyle style)
    {
        yield return (Loc.T("homeLook.default"), style.Font.Length == 0, () => SetHomeInfo(() => style.Font = string.Empty));
        foreach (string font in HomeInfoSettings.Fonts)
        {
            yield return (font, style.Font == font, () => SetHomeInfo(() => style.Font = font));
        }
    }

    private IEnumerable<(string, bool, Action)> SizeList(HomeElementStyle style, string key)
    {
        yield return (Loc.T("homeLook.default"), style.Size <= 0, () => SetHomeInfo(() => style.Size = 0));
        foreach (double size in SizeChoices)
        {
            yield return ($"{size:0}", Math.Abs(style.Size - size) < 0.5, () => SetHomeInfo(() => style.Size = size));
        }
    }

    private IEnumerable<(string, bool, Action)> PositionChoices(HomeElementStyle style)
    {
        foreach (string id in HomeInfoSettings.Positions)
        {
            yield return (Loc.T("homePos." + id), style.Position == id, () => SetHomeInfo(() => style.Position = id));
        }
    }

    /// <summary>Tekstovi gumba i pregled prema trenutnim postavkama.</summary>
    internal void RefreshHomeLook()
    {
        HomeInfoSettings h = _settings.HomeInfo;
        foreach ((string key, Func<HomeInfoSettings, HomeElementStyle> style) in Elements)
        {
            HomeElementStyle s = style(h);
            _homeLookButtons[key + ".font"].Content = s.Font.Length == 0 ? Loc.T("homeLook.default") : s.Font;
            _homeLookButtons[key + ".size"].Content = s.Size <= 0 ? Loc.T("homeLook.default") : $"{s.Size:0}";
            _homeLookButtons[key + ".pos"].Content = Loc.T("homePos." + s.Position);
        }

        if (_homePreview is { } preview)
        {
            // Pregled prije spremanja: isti sloj kao na početnom ekranu, umanjen, s primjerom podataka.
            preview.ApplyStyle(h, 0.55);
            DateTime now = DateTime.Now;
            preview.TimeText.Text = now.ToString("t", Loc.Culture);
            preview.DateText.Text = now.ToString("ddd, d. MMM", Loc.Culture);
            preview.WeatherText.Text = "☀ 14 °C · " + Loc.T("weather.clear");
            var sample = new List<(string Icon, string Text)>();
            if (h.WeatherDetails.Contains("feels")) { sample.Add((HomeInfoService.IconFor("feels"), Loc.T("weather.feels", "12 °C"))); }
            if (h.WeatherDetails.Contains("minmax")) { sample.Add((HomeInfoService.IconFor("minmax"), Loc.T("weather.minmax", "9 °C", "15 °C"))); }
            if (h.WeatherDetails.Contains("wind")) { sample.Add((HomeInfoService.IconFor("wind"), Loc.T("weather.wind", "12", "km/h", "↗"))); }
            preview.SetDetails(sample);
            preview.WeatherText.Visibility = Visibility.Visible;
        }
    }

    private void OnHomeLookDefault(object sender, RoutedEventArgs e)
    {
        SetHomeInfo(() => _settings.HomeInfo.ResetStyle());
        BuildHomeLook();
    }

    internal HomeInfoLayer? HomePreviewForTests => _homePreview;

    internal void ShowHomeLookForTests() => HomePreviewHost.BringIntoView();
}
