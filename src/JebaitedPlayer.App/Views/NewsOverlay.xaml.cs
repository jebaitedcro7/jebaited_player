using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JebaitedPlayer.Services.News;

namespace JebaitedPlayer.Views;

/// <summary>
/// Kartica obavijesti na Home ekranu (News Card) sa strelicom do broja verzije. Položaj je zadan samo donjim rubom kartice
/// (<see cref="SetLayout"/>): iznad trake playera kad je ima, inače tik iznad donjeg reda. Kad se cilj promijeni (player se
/// pojavi ili nestane), kartica se glatko pomakne, a strelica se produži ili skrati. Bez animacije ako je Windows isključuje.
/// </summary>
public partial class NewsOverlay : UserControl
{
    private const double MaxCardWidth = 366;
    private const double ShortArrow = 56;

    private double _targetBottom = double.NaN;
    private double _versionX;
    private double _versionTop;

    public NewsOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Reposition();
    }

    public static readonly DependencyProperty CardBottomProperty =
        DependencyProperty.Register(nameof(CardBottom), typeof(double), typeof(NewsOverlay),
            new PropertyMetadata(0.0, (d, _) => ((NewsOverlay)d).Reposition()));

    /// <summary>Donji rub kartice (px od vrha prekrivača); animira se.</summary>
    public double CardBottom
    {
        get => (double)GetValue(CardBottomProperty);
        set => SetValue(CardBottomProperty, value);
    }

    public NewsItem? Item { get; private set; }

    public event Action<NewsItem>? Opened;

    public event Action<NewsItem>? Dismissed;

    /// <summary>Zadnja izmjerena geometrija (za provjeru).</summary>
    internal Rect CardBounds => new(Canvas.GetLeft(Card), Canvas.GetTop(Card), Card.Width, Card.ActualHeight);

    internal bool ArrowVisible => Shaft.Visibility == Visibility.Visible;

    internal bool ArrowIsStraight => Shaft.Data is LineGeometry;

    internal Point ArrowTip => _tip;

    internal double TargetBottom => _targetBottom;

    /// <summary>Dijagnostika: glatki pomak kartice na novi donji rub (isto što se događa kad se traka playera pojavi ili nestane).</summary>
    internal void SlideForTests(double cardBottom) => SetLayout(cardBottom, _versionX, _versionTop);

    internal Button CloseButtonForTests => CloseButton;

    internal Button MoreButtonForTests => MoreButton;

    internal string TitleForTests => TitleText.Text;

    private Point _tip;

    public void Show(NewsItem item)
    {
        Item = item;
        TagText.Text = item.CategoryText.ToUpper(Loc.Culture);
        TitleText.Text = item.Title;
        BodyText.Text = item.Body;
        IconText.Text = item.Category switch
        {
            NewsCategory.Important => "",
            NewsCategory.Update => "",
            NewsCategory.New => "",
            _ => "",
        };
        TagBox.Background = new SolidColorBrush(item.Category switch
        {
            NewsCategory.Important => Color.FromArgb(0x55, 0xFF, 0x8A, 0x80),
            NewsCategory.Update => Color.FromArgb(0x55, 0x7F, 0xB4, 0xFF),
            NewsCategory.New => Color.FromArgb(0x55, 0x7B, 0xD8, 0x8F),
            _ => Color.FromArgb(0x55, 0xE8, 0xC2, 0x6A),
        });
        Visibility = Visibility.Visible;
        Reposition();
    }

    public void Hide()
    {
        Item = null;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Novi cilj: donji rub kartice i položaj broja verzije (sve u koordinatama ovog prekrivača).
    /// Prvi put (i kad se kartica tek prikazala) odmah, kasnije glatko.
    /// </summary>
    public void SetLayout(double cardBottom, double versionCenterX, double versionTop, bool immediate = false)
    {
        _versionX = versionCenterX;
        _versionTop = versionTop;
        if (immediate || double.IsNaN(_targetBottom) || Visibility != Visibility.Visible || !SystemParameters.ClientAreaAnimation
            || Math.Abs(cardBottom - _targetBottom) < 1)
        {
            BeginAnimation(CardBottomProperty, null);
            _targetBottom = cardBottom;
            CardBottom = cardBottom;
            Reposition();
            return;
        }

        _targetBottom = cardBottom;
        BeginAnimation(CardBottomProperty, new DoubleAnimation(cardBottom, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    private void Reposition()
    {
        if (Item is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        double width = Math.Min(MaxCardWidth, Math.Max(220, ActualWidth - 24));
        Card.Width = width;
        Card.Measure(new Size(width, double.PositiveInfinity));
        double height = Card.DesiredSize.Height;

        // Desni rub kartice tik iza broja verzije, da strelica izlazi iz ravnog dijela donjeg ruba (ne iz zaobljenog kuta).
        double right = Math.Clamp(_versionX + 22, width + 8, ActualWidth - 8);
        double left = right - width;
        double top = CardBottom - height;
        Canvas.SetLeft(Card, left);
        Canvas.SetTop(Card, top);
        DrawArrow(left, right, CardBottom);
    }

    /// <summary>
    /// Kratka strelica (kartica nisko): ravna, okomito iz donjeg ruba kartice, a sama crta izlazi iz tijela strelice.
    /// Duga (kartica visoko): blago savijena „S” crta koja se pri kartici stapa (prozirno → puno) i završava vrhom strelice.
    /// Vrh uvijek pokazuje prema dolje, na broj verzije.
    /// </summary>
    private void DrawArrow(double cardLeft, double cardRight, double cardBottom)
    {
        double endX = _versionX;
        double endY = _versionTop - 3;
        double gap = endY - cardBottom;
        bool visible = gap >= 12 && endX > cardLeft + 16;
        Shaft.Visibility = Head.Visibility = Anchor.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _tip = new Point(endX, endY);
        if (!visible)
        {
            return;
        }

        const double headLength = 9, headHalf = 5;
        double shaftEnd = endY - headLength + 2; // vrh strelice se uklapa u kraj crte
        Head.Points = [new Point(endX - headHalf, endY - headLength), new Point(endX, endY), new Point(endX + headHalf, endY - headLength),
            new Point(endX, endY - headLength + 2.6)];

        if (gap <= ShortArrow)
        {
            // Crta ide ravno iz strelice: bez ugla i bez „kuke” sa strane.
            Shaft.Data = new LineGeometry(new Point(endX, cardBottom), new Point(endX, shaftEnd));
            Canvas.SetLeft(Anchor, endX - 3.5);
            Canvas.SetTop(Anchor, cardBottom - 3.5);
            Anchor.Visibility = Visibility.Collapsed; // kratka strelica nema točku: crta počinje na rubu kartice
        }
        else
        {
            double startX = Math.Max(cardLeft + 24, cardRight - 44);
            double mid = cardBottom + ((shaftEnd - cardBottom) * 0.5);
            var figure = new PathFigure { StartPoint = new Point(startX, cardBottom), IsClosed = false };
            figure.Segments.Add(new BezierSegment(new Point(startX, mid), new Point(endX, mid), new Point(endX, shaftEnd), true));
            Shaft.Data = new PathGeometry([figure]);
            Canvas.SetLeft(Anchor, startX - 3.5);
            Canvas.SetTop(Anchor, cardBottom - 3.5);
        }

        // Crta se pri kartici stapa (prozirno), prema verziji postaje puna.
        Shaft.OpacityMask = new LinearGradientBrush(
            [new GradientStop(Color.FromArgb(0x40, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0xE6, 0, 0, 0), 0.4),
             new GradientStop(Colors.Black, 1)],
            new Point(0, cardBottom), new Point(0, shaftEnd)) { MappingMode = BrushMappingMode.Absolute };
    }

    private void OnCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Item is { } item && !e.Handled)
        {
            e.Handled = true;
            Opened?.Invoke(item);
        }
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (Item is { } item)
        {
            e.Handled = true;
            Opened?.Invoke(item);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (Item is { } item)
        {
            e.Handled = true;
            Dismissed?.Invoke(item);
        }
    }
}
