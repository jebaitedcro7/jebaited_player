using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;

namespace JebaitedPlayer.Controls;

/// <summary>
/// 2.1.7: vrijeme, datum i prognoza na početnom ekranu. Svaki element ima vlastiti font, veličinu i mjesto (8 sidrišta oko ruba,
/// bez sredine gdje je logo); elementi na istom mjestu slažu se jedan ispod drugog. Isti sloj služi i za pregled u postavkama.
/// </summary>
public sealed class HomeInfoLayer : Grid
{
    private readonly Dictionary<string, StackPanel> _anchors = [];
    private readonly StackPanel _weatherBlock = new();
    private double _scale = 1;

    public HomeInfoLayer()
    {
        IsHitTestVisible = false;
        TimeText = new TextBlock { FontWeight = FontWeights.Light };
        DateText = new TextBlock();
        WeatherText = new TextBlock { Visibility = Visibility.Collapsed };
        WeatherDetails = new TextBlock { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 3, 0, 0), LineHeight = double.NaN };
        WeatherSource = new TextBlock { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 3, 0, 0), Text = Loc.T("weather.source") };
        DateText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        WeatherDetails.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        WeatherSource.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _weatherBlock.Children.Add(WeatherText);
        _weatherBlock.Children.Add(WeatherDetails);
        _weatherBlock.Children.Add(WeatherSource);

        foreach (string id in HomeInfoSettings.Positions)
        {
            var panel = new StackPanel
            {
                HorizontalAlignment = id[1] switch { 'l' => HorizontalAlignment.Left, 'r' => HorizontalAlignment.Right, _ => HorizontalAlignment.Center },
                VerticalAlignment = id[0] switch { 't' => VerticalAlignment.Top, 'b' => VerticalAlignment.Bottom, _ => VerticalAlignment.Center },
                Margin = new Thickness(26, 16, 26, 16),
            };
            // "tl", "tc", "tr", "ml", "mr", "bl", "bc", "br"
            _anchors[id] = panel;
            Children.Add(panel);
        }

        Loc.LanguageChanged += OnLanguage;
        Unloaded += (_, _) => Loc.LanguageChanged -= OnLanguage;
        ApplyStyle(new HomeInfoSettings());
    }

    public TextBlock TimeText { get; }

    public TextBlock DateText { get; }

    /// <summary>Redak sa simbolom, temperaturom i opisom.</summary>
    public TextBlock WeatherText { get; }

    /// <summary>Dodatni podaci prognoze, jedan po retku.</summary>
    public TextBlock WeatherDetails { get; }

    public TextBlock WeatherSource { get; }

    /// <summary>Mjesto na kojem je pojedini element ("time", "date", "weather"). Za provjeru.</summary>
    public string PositionOf(string element) => _anchors.First(a => a.Value.Children.Contains(element switch
    {
        "time" => TimeText,
        "date" => DateText,
        _ => _weatherBlock,
    })).Key;

    private void OnLanguage() => WeatherSource.Text = Loc.T("weather.source");

    /// <summary>Font, veličina i mjesto svakog elementa iz postavki; <paramref name="scale"/> smanjuje sve za pregled u postavkama.</summary>
    public void ApplyStyle(HomeInfoSettings s, double scale = 1)
    {
        _scale = scale;
        double timeSize = Size(s.TimeStyle, HomeInfoSettings.DefaultTimeSize);
        double dateSize = Size(s.DateStyle, HomeInfoSettings.DefaultDateSize);
        double weatherSize = Size(s.WeatherStyle, HomeInfoSettings.DefaultWeatherSize);
        StyleText(TimeText, s.TimeStyle, timeSize, light: true);
        StyleText(DateText, s.DateStyle, dateSize);
        StyleText(WeatherText, s.WeatherStyle, weatherSize);
        StyleText(WeatherDetails, s.WeatherStyle, Math.Max(8, weatherSize * 0.92));
        StyleText(WeatherSource, s.WeatherStyle, Math.Max(7, weatherSize * 0.75));
        Margin = new Thickness(0);

        foreach (StackPanel panel in _anchors.Values)
        {
            panel.Children.Clear();
            panel.Margin = new Thickness(26 * scale, 16 * scale, 26 * scale, 16 * scale);
        }

        Place(TimeText, s.TimeStyle.Position);
        Place(DateText, s.DateStyle.Position);
        Place(_weatherBlock, s.WeatherStyle.Position);
        _weatherBlock.Margin = new Thickness(0, 8 * scale, 0, 0);
        TimeText.Margin = new Thickness(0);
        DateText.Margin = new Thickness(0);
    }

    private double Size(HomeElementStyle style, double fallback) => (style.Size > 0 ? style.Size : fallback) * _scale;

    private static void StyleText(TextBlock text, HomeElementStyle style, double size, bool light = false)
    {
        text.FontSize = size;
        if (style.Font.Length > 0)
        {
            text.FontFamily = new FontFamily(style.Font);
            text.FontWeight = FontWeights.Normal;
        }
        else
        {
            text.ClearValue(TextBlock.FontFamilyProperty);
            text.FontWeight = light ? FontWeights.Light : FontWeights.Normal;
        }
    }

    private void Place(UIElement element, string position)
    {
        string id = _anchors.ContainsKey(position) ? position : "tl";
        StackPanel panel = _anchors[id];
        panel.Children.Add(element);
        TextAlignment alignment = id[1] switch { 'l' => TextAlignment.Left, 'r' => TextAlignment.Right, _ => TextAlignment.Center };
        if (element is TextBlock block)
        {
            block.TextAlignment = alignment;
        }
        else
        {
            foreach (TextBlock child in _weatherBlock.Children.OfType<TextBlock>())
            {
                child.TextAlignment = alignment;
            }
        }
    }
}
