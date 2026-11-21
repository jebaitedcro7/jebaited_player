using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.Views;

/// <summary>
/// 2.1.7: oznaka LIVE za radio. Crvena točka lagano pulsira (samo promjena prozirnosti, 20 sličica u sekundi), a animacija
/// radi samo dok je oznaka vidljiva. Kad radio ne svira, oznaka je skrivena.
/// </summary>
public sealed class LiveBadge : Border
{
    private static readonly Brush Red = Frozen(Color.FromRgb(0xE5, 0x39, 0x35));
    private readonly Ellipse _dot = new() { Width = 8, Height = 8, Fill = Red, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label = new() { FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Red, VerticalAlignment = VerticalAlignment.Center };

    public LiveBadge()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        _dot.Margin = new Thickness(0, 0, 5, 0);
        row.Children.Add(_dot);
        row.Children.Add(_label);
        Child = row;
        Padding = new Thickness(7, 1, 7, 1);
        CornerRadius = new CornerRadius(8);
        BorderBrush = Frozen(Color.FromArgb(0x90, 0xE5, 0x39, 0x35));
        BorderThickness = new Thickness(1);
        Background = Frozen(Color.FromArgb(0x26, 0xE5, 0x39, 0x35));
        VerticalAlignment = VerticalAlignment.Center;
        Visibility = Visibility.Collapsed;
        _label.Text = Loc.T("mini.live");
        Loc.LanguageChanged += OnLanguage;
        Unloaded += (_, _) => Loc.LanguageChanged -= OnLanguage;
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    /// <summary>Točka pulsira (oznaka je vidljiva). Za provjeru.</summary>
    public bool IsPulsing { get; private set; }

    private void OnLanguage() => _label.Text = Loc.T("mini.live");

    private void UpdateAnimation()
    {
        if (IsVisible)
        {
            var pulse = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(850))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(pulse, 20);
            _dot.BeginAnimation(OpacityProperty, pulse);
            IsPulsing = true;
        }
        else
        {
            _dot.BeginAnimation(OpacityProperty, null);
            IsPulsing = false;
        }
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
