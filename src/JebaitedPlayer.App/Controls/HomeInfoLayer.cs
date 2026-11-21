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
        WeatherDetails = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 3, 0, 0) };
        // 2.1.9: natpis "Weather data by Open-Meteo.com" je skriven na Home ekranu (izvor je naveden u postavkama i u THIRD_PARTY_NOTICES).
        WeatherSource = new TextBlock { Visibility = Visibility.Collapsed };
        DateText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _weatherBlock.Children.Add(WeatherText);
        _weatherBlock.Children.Add(WeatherDetails);

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

    /// <summary>Dodatni podaci prognoze, jedan po retku, svaki s ikonom ispred.</summary>
    public StackPanel WeatherDetails { get; }

    /// <summary>Više se ne prikazuje (skriveno na zahtjev); ostaje samo da kod koji ga postavlja radi.</summary>
    public TextBlock WeatherSource { get; }

    private double _detailSize = 14;
    private FontFamily? _detailFont;
    private TextAlignment _detailAlignment = TextAlignment.Left;
    private IReadOnlyList<(string Icon, string Text)> _details = [];

    /// <summary>Postavi retke prognoze: ikona (emoji) u stupcu fiksne širine, pa tekst.</summary>
    public void SetDetails(IReadOnlyList<(string Icon, string Text)> details)
    {
        _details = details;
        RebuildDetails();
    }

    /// <summary>Tekst svih redaka (za provjeru).</summary>
    public string DetailsText => string.Join(Environment.NewLine, _details.Select(d => d.Text));

    private void RebuildDetails()
    {
        WeatherDetails.Children.Clear();
        foreach ((string icon, string text) in _details)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1, 0, 1),
                HorizontalAlignment = _detailAlignment switch
                {
                    TextAlignment.Right => HorizontalAlignment.Right,
                    TextAlignment.Center => HorizontalAlignment.Center,
                    _ => HorizontalAlignment.Left,
                },
            };
            var iconBlock = new TextBlock
            {
                Text = icon, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = _detailSize,
                Width = _detailSize * 1.5, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var textBlock = new TextBlock { Text = text, FontSize = _detailSize, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            if (_detailFont != null)
            {
                textBlock.FontFamily = _detailFont;
            }

            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            row.Children.Add(iconBlock);
            row.Children.Add(textBlock);
            WeatherDetails.Children.Add(row);
        }

        WeatherDetails.Visibility = _details.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Mjesto na kojem je pojedini element ("time", "date", "weather"). Za provjeru.</summary>
    public string PositionOf(string element) => _anchors.First(a => a.Value.Children.Contains(element switch
    {
        "time" => TimeText,
        "date" => DateText,
        _ => _weatherBlock,
    })).Key;

    private void OnLanguage() { }

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
        _detailSize = Math.Max(8, weatherSize * 0.92);
        _detailFont = s.WeatherStyle.Font.Length > 0 ? new FontFamily(s.WeatherStyle.Font) : null;
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

            _detailAlignment = alignment;
            RebuildDetails();
        }
    }
}
