using System.Windows;

namespace JebaitedPlayer.Views;

/// <summary>Postavke → Obavijesti: isti popis i isto stanje kao tipka „Obavijesti” na početnom ekranu.</summary>
public partial class PlayerSettingsWindow
{
    private NewsListPanel? _newsList;

    internal NewsListPanel? NewsListForTests => _newsList;

    private void LoadNews()
    {
        _newsList = new NewsListPanel(showStatusText: true);
        _newsList.Bind(_host.News);
        _newsList.ItemInvoked += _host.OpenNewsItem;
        NewsListHost.Content = _newsList;
    }

    private void OnNewsMarkAll(object sender, RoutedEventArgs e) => _host.News.MarkAllRead();
}
