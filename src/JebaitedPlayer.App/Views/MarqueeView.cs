using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JebaitedPlayer.Views;

/// <summary>
/// 2.1.7: naslov pjesme koji se neprekidno vrti u krug. Tekst je u traci dvaput (razmak između), pa se pri kraju nastavlja
/// od početka bez trzaja i bez praznog prostora. Ako tekst stane, stoji (bez animacije, bez CPU-a). Font, veličina i boja
/// dolaze iz TextElement.* svojstava (nasljeđuju se), pa se postavljaju kao na običnom TextBlocku.
/// </summary>
public sealed class MarqueeView : Border
{
    /// <summary>Razmak između kraja teksta i njegova ponovnog početka.</summary>
    internal const double Gap = 56;

    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _first = new() { TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock _second = new() { TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(Gap, 0, 0, 0) };
    private readonly TranslateTransform _shift = new();
    private string _text = string.Empty;

    public MarqueeView()
    {
        ClipToBounds = true;
        _strip.RenderTransform = _shift;
        _strip.Children.Add(_first);
        _strip.Children.Add(_second);
        _canvas.Children.Add(_strip);
        Child = _canvas;
        SizeChanged += (_, _) => Restart();
        IsVisibleChanged += (_, _) => Restart();
    }

    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (value == _text)
            {
                return;
            }

            _text = value;
            _first.Text = value;
            _second.Text = value;
            Restart();
        }
    }

    /// <summary>Tekst se kreće (ne stane u prostor).</summary>
    public bool IsScrolling { get; private set; }

    /// <summary>Trajanje jednog kruga (s); 0 dok tekst stoji. Za provjeru brzine.</summary>
    public double CycleSeconds { get; private set; }

    /// <summary>Širina jednog primjerka teksta (px). Za provjeru.</summary>
    public double TextWidth { get; private set; }

    /// <summary>Font ili veličina su se promijenili: ponovno izmjeri.</summary>
    public void Refresh() => Restart();

    private void Restart()
    {
        _shift.BeginAnimation(TranslateTransform.XProperty, null);
        _shift.X = 0;
        IsScrolling = false;
        CycleSeconds = 0;

        _strip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _first.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        TextWidth = _first.DesiredSize.Width;
        double top = Math.Max(0, (ActualHeight - _first.DesiredSize.Height) / 2);
        Canvas.SetTop(_strip, top);

        bool scroll = IsVisible && ActualWidth > 0 && _text.Length > 0 && TextWidth > ActualWidth + 2;
        _second.Visibility = scroll ? Visibility.Visible : Visibility.Collapsed;
        if (!scroll)
        {
            return;
        }

        // Brzina prema duljini: kratki tekstovi sporije (lakše za čitanje), dugi malo brže, da krug ne traje predugo.
        double distance = TextWidth + Gap;
        double pixelsPerSecond = Math.Clamp(34 + TextWidth / 25, 34, 70);
        var animation = new DoubleAnimation(0, -distance, TimeSpan.FromSeconds(distance / pixelsPerSecond))
        {
            BeginTime = TimeSpan.FromSeconds(1.5),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Timeline.SetDesiredFrameRate(animation, 30);
        _shift.BeginAnimation(TranslateTransform.XProperty, animation);
        IsScrolling = true;
        CycleSeconds = distance / pixelsPerSecond;
    }
}
