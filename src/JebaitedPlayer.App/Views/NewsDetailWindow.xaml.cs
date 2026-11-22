using System.Windows;
using System.Windows.Media;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services.News;

namespace JebaitedPlayer.Views;

public partial class NewsDetailWindow : Window
{
    private readonly NewsItem _item;
    private readonly Action<NewsItem> _runAction;

    internal NewsDetailWindow(NewsItem item, Action<NewsItem> runAction)
    {
        _item = item;
        _runAction = runAction;
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        TagText.Text = item.CategoryText.ToUpper(Loc.Culture);
        TagBox.Background = new SolidColorBrush(item.Category switch
        {
            NewsCategory.Important => Color.FromArgb(0x55, 0xFF, 0x8A, 0x80),
            NewsCategory.Update => Color.FromArgb(0x55, 0x7F, 0xB4, 0xFF),
            NewsCategory.New => Color.FromArgb(0x55, 0x7B, 0xD8, 0x8F),
            _ => Color.FromArgb(0x55, 0xE8, 0xC2, 0x6A),
        });
        TitleText.Text = item.Title;
        DetailText.Text = item.Detail;
        bool hasAction = item.Action != NewsAction.None && item.ActionText.Length > 0;
        ActionButton.Visibility = hasAction ? Visibility.Visible : Visibility.Collapsed;
        ActionButton.Content = item.ActionText;
        this.OnLanguageChanged(() =>
        {
            TagText.Text = item.CategoryText.ToUpper(Loc.Culture);
            TitleText.Text = item.Title;
            DetailText.Text = item.Detail;
            ActionButton.Content = item.ActionText;
        });
    }

    internal string TitleForTests => TitleText.Text;

    internal Visibility ActionVisibility => ActionButton.Visibility;

    internal void ClickActionForTests() => OnActionClick(this, new RoutedEventArgs());

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        Close();
        _runAction(_item);
    }
}
