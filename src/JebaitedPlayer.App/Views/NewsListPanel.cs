using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services.News;

namespace JebaitedPlayer.Views;

/// <summary>
/// Popis obavijesti (nepročitane, pročitane i odbačene): na Home ekranu u maloj skočnoj ploči iza tipke „Obavijesti”
/// i u Postavke → Obavijesti. Klik na redak otvara detalje.
/// </summary>
public sealed class NewsListPanel : UserControl
{
    private readonly StackPanel _rows = new();
    private readonly bool _showStatusText;
    private NewsCenter? _center;

    public NewsListPanel(bool showStatusText)
    {
        _showStatusText = showStatusText;
        Content = _rows;
        Unloaded += (_, _) => Detach();
        Loaded += (_, _) => Attach();
        _languageHandler = () => Dispatcher.BeginInvoke(Refresh);
    }

    private readonly Action _languageHandler;

    public event Action<NewsItem>? ItemInvoked;

    internal int RowCount => _rows.Children.OfType<Button>().Count();

    /// <summary>Dijagnostika: redovi (id → stanje).</summary>
    internal IReadOnlyList<(string Id, NewsStatus Status)> Rows => _rows.Children.OfType<Button>()
        .Select(b => ((NewsItem)b.Tag, _center!.StatusOf(((NewsItem)b.Tag).Id))).Select(t => (t.Item1.Id, t.Item2)).ToList();

    internal void ClickRowForTests(string id)
    {
        NewsItem? item = _rows.Children.OfType<Button>().Select(b => (NewsItem)b.Tag).FirstOrDefault(i => i.Id == id);
        if (item is not null)
        {
            ItemInvoked?.Invoke(item);
        }
    }

    public void Bind(NewsCenter center)
    {
        Detach();
        _center = center;
        if (IsLoaded)
        {
            Attach();
        }

        Refresh();
    }

    private void Attach()
    {
        if (_center is not null)
        {
            _center.Changed -= OnChanged;
            _center.Changed += OnChanged;
            Loc.LanguageChanged -= _languageHandler;
            Loc.LanguageChanged += _languageHandler;
            Refresh();
        }
    }

    private void Detach()
    {
        Loc.LanguageChanged -= _languageHandler;
        if (_center is not null)
        {
            _center.Changed -= OnChanged;
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Refresh);

    public void Refresh()
    {
        _rows.Children.Clear();
        if (_center is null)
        {
            return;
        }

        IReadOnlyList<NewsItem> items = _center.All;
        if (items.Count == 0)
        {
            var empty = new TextBlock { Text = Loc.T("news.empty"), Margin = new Thickness(14, 12, 14, 12) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            _rows.Children.Add(empty);
            return;
        }

        foreach (NewsItem item in items)
        {
            _rows.Children.Add(Row(item, _center.StatusOf(item.Id)));
        }
    }

    private Button Row(NewsItem item, NewsStatus status)
    {
        var dot = new Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 6, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        switch (status)
        {
            case NewsStatus.Unread:
                dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
                break;
            case NewsStatus.Read:
                dot.SetResourceReference(Shape.StrokeProperty, "TextSecondaryBrush");
                dot.StrokeThickness = 1.2;
                break;
            default:
                dot.SetResourceReference(Shape.StrokeProperty, "TextSecondaryBrush");
                dot.StrokeThickness = 1.2;
                dot.StrokeDashArray = [2, 2];
                break;
        }

        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 13.5,
            FontWeight = status == NewsStatus.Unread ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var body = new TextBlock { Text = item.Body, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
        body.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var texts = new StackPanel();
        texts.Children.Add(title);
        texts.Children.Add(body);

        var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(dot);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        if (_showStatusText)
        {
            var state = new TextBlock
            {
                Text = Loc.T("news.status." + status.ToString().ToLowerInvariant()),
                FontSize = 11,
                Margin = new Thickness(12, 3, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            state.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(state, 2);
            grid.Children.Add(state);
        }

        var button = new Button { Content = grid, Tag = item, Opacity = status == NewsStatus.Unread ? 1 : 0.72 };
        button.SetResourceReference(StyleProperty, "ListRowButton");
        AutomationProperties.SetName(button, $"{item.Title}, {Loc.T("news.status." + status.ToString().ToLowerInvariant())}");
        button.Click += (_, _) => ItemInvoked?.Invoke(item);
        return button;
    }
}
