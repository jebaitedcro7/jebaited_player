using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JebaitedPlayer;

/// <summary>Bočna traka početnog ekrana: uska (samo ikone); pod mišem se glatko proširi i pokaže nazive, bez pomicanja ostatka ekrana.</summary>
public partial class MainWindow
{
    private const double RailCollapsedWidth = 56;
    private const double RailMinExpandedWidth = 200;
    private static readonly TimeSpan RailOpenTime = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan RailCloseTime = TimeSpan.FromMilliseconds(200);

    private double _railTarget = RailCollapsedWidth;

    private void InitHomeRail()
    {
        // Vidljiva širina se animira izrezom (Clip), a ne širinom elementa: širina bi pri svakoj sličici ponovno slagala cijeli početni
        // ekran (na velikom zaslonu uz video to trza), dok izrez ne traži novo slaganje, samo iscrtavanje.
        HomeRail.Clip = new RectangleGeometry(new Rect(0, 0, RailCollapsedWidth, 6000));

        HomeRail.MouseEnter += (_, _) => SetRailExpanded(true);
        HomeRail.MouseLeave += (_, _) => SetRailExpanded(false);
    }

    internal bool IsHomeRailExpanded { get; private set; }

    /// <summary>Označi aktivnu stavku trake (IPTV dok je panel otvoren) naglasnom bojom.</summary>
    private void UpdateHomeRailActive() =>
        HomeIptvButton.SetResourceReference(Control.ForegroundProperty, IsIptvVisible ? "AccentBrush" : "TextPrimaryBrush");

    internal void SetRailExpanded(bool expanded)
    {
        IsHomeRailExpanded = expanded;
        if (expanded)
        {
            SetRailLabels(true);
        }

        // Širina i nazivi idu zajedno: širina lagano ubrzava i usporava, nazivi se pojavljuju s malim kašnjenjem (i nestaju prije zatvaranja).
        TimeSpan time = expanded ? RailOpenTime : RailCloseTime;
        if (expanded)
        {
            _railTarget = RailExpandedTarget();
            HomeRail.Width = _railTarget;
        }

        var reveal = new RectAnimation(new Rect(0, 0, expanded ? _railTarget : RailCollapsedWidth, 6000), time)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        reveal.Completed += (_, _) =>
        {
            if (!IsHomeRailExpanded)
            {
                SetRailLabels(false);
            }
        };
        if (HomeRail.Clip is not RectangleGeometry clip || clip.IsFrozen)
        {
            clip = new RectangleGeometry(new Rect(0, 0, RailCollapsedWidth, 6000));
            HomeRail.Clip = clip;
        }

        clip.BeginAnimation(RectangleGeometry.RectProperty, reveal);

        // Pozadina: uska poluprozirna ploča prelazi u široku prelijevajuću (križanje, bez skoka).
        HomeRailWideBg.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(expanded ? 1 : 0, time));
        HomeRailNarrowBg.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(expanded ? 0 : 1, time));

        var fade = new DoubleAnimation(expanded ? 1 : 0, TimeSpan.FromMilliseconds(expanded ? 170 : 90))
        {
            BeginTime = TimeSpan.FromMilliseconds(expanded ? 70 : 0),
        };
        if (expanded)
        {
            fade.From = 0;
        }
        foreach (FrameworkElement label in RailLabels())
        {
            label.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }

    /// <summary>Širina proširene trake prema najduljem nazivu (ovisi o jeziku), da tekst nikad ne dođe tik do ruba.</summary>
    private double RailExpandedTarget()
    {
        double longest = 0;
        foreach (FrameworkElement label in RailLabels())
        {
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            longest = Math.Max(longest, label.DesiredSize.Width);
        }

        // 6 (rub) + 7 (uvlaka gumba) + 28 (ikona) + 10 (razmak) + naziv + 34 (razmak do desnog ruba i obruba).
        return Math.Max(RailMinExpandedWidth, 6 + 10 + 28 + 10 + longest + 44);
    }

    private IEnumerable<FrameworkElement> RailLabels()
    {
        foreach (UIElement child in HomeOpenButtons.Children)
        {
            if (child is Button { Content: StackPanel { Children.Count: > 1 } sp } btn && btn.Visibility == Visibility.Visible && sp.Children[1] is FrameworkElement label)
            {
                yield return label;
            }
        }
    }

    private void SetRailLabels(bool visible)
    {
        foreach (FrameworkElement label in RailLabels())
        {
            label.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Dijagnostika: trenutna širina trake (usred animacije je između uske i široke).</summary>
    internal double HomeRailWidthForTests => HomeRail.Clip?.Bounds.Width ?? HomeRail.ActualWidth;
}
