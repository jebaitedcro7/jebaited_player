using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services.News;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Obavijesti na Home ekranu: kartica (News Card) sa strelicom do broja verzije, tipka „Obavijesti” s brojem nepročitanih i
/// popis obavijesti. Kartica stoji iznad trake playera ako je ima, a inače nisko, tik iznad donjeg reda; kad se player pojavi
/// ili nestane, glatko se pomakne. Izvori su samo ugrađeni popis i nadogradnja iz <see cref="JebaitedPlayer.Services.UpdateService"/>.
/// </summary>
public partial class MainWindow
{
    /// <summary>Razmak kartice od trake playera (visoki položaj) i od donjeg reda (niski, ostavlja mjesta za strelicu).</summary>
    private const double NewsGapAboveBar = 13, NewsGapAboveRow = 25;

    private NewsCenter? _news;
    private NewsDetailWindow? _newsDetail;
    private bool _newsActed;
    private bool _newsPlaceImmediately;
    private (double Bottom, double X, double Top) _newsLastLayout;

    internal NewsCenter News => _news ??= new NewsCenter(FileNewsStateStore.Default);

    NewsCenter ISettingsHost.News => News;

    void ISettingsHost.OpenNewsItem(NewsItem item) => OpenNewsDetail(item);

    internal NewsOverlay NewsOverlayForTests => HomeNews;

    internal NewsDetailWindow? NewsDetailForTests => _newsDetail;

    internal NewsListPanel NewsPopupListForTests => (NewsListPanel)HomeNewsListHost.Content;

    internal bool NewsPopupOpen => HomeNewsPopup.IsOpen;

    private void InitializeNews()
    {
        NewsCenter news = News;
        HomeNews.Opened += OpenNewsDetail;
        HomeNews.Dismissed += item =>
        {
            news.Dismiss(item.Id);
            _newsActed = true;
            HomeNews.Hide();
        };

        var list = new NewsListPanel(showStatusText: false);
        list.Bind(news);
        list.ItemInvoked += item =>
        {
            HomeNewsPopup.IsOpen = false;
            OpenNewsDetail(item);
        };
        HomeNewsListHost.Content = list;

        news.Changed += (_, _) => Dispatcher.BeginInvoke(UpdateNewsBadge);
        Updates.Changed += (_, _) => Dispatcher.BeginInvoke(SyncNewsUpdate);
        SyncNewsUpdate();
        UpdateNewsBadge();
    }

    /// <summary>Nova verzija: samo ona koju je UpdateService stvarno preuzeo (Store izdanje nadograđuje Store, pa je nema).</summary>
    private void SyncNewsUpdate() => News.SetUpdateVersion(Updates.IsStore ? null : Updates.ReadyVersion);

    private void UpdateNewsBadge()
    {
        int unread = News.UnreadCount;
        HomeNewsBadge.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        HomeNewsBadgeText.Text = unread.ToString(Loc.Culture);
        HomeNewsButton.ToolTip = unread > 0 ? Loc.T("news.unreadCount", unread) : Loc.T("news.buttonTooltip");
    }

    /// <summary>Kartica koja se smije prikazati: nakon što korisnik jednom reagira, u istom pokretanju dolaze samo važno i nove verzije.</summary>
    private NewsItem? ChooseNewsCard()
    {
        NewsItem? card = News.ActiveCard;
        return card is not null && _newsActed && card.Category >= NewsCategory.New ? null : card;
    }

    /// <summary>Poziva se iz UI timera (≈ 5 puta u sekundi) dok je Home ekran vidljiv: što se prikazuje i gdje.</summary>
    private void UpdateNews()
    {
        NewsItem? card = WelcomePanel.IsVisible ? ChooseNewsCard() : null;
        if (card is null)
        {
            if (HomeNews.Item is not null)
            {
                HomeNews.Hide();
            }

            return;
        }

        if (HomeNews.Item?.Id != card.Id)
        {
            _newsPlaceImmediately = true; // tek prikazana kartica se postavlja odmah (ne klizi s prijašnjeg mjesta)
            HomeNews.Show(card);
            News.NoteShown(card.Id);
            _newsLastLayout = default;
        }

        if (HomeNews.ActualWidth <= 0)
        {
            return;
        }

        double rowTop = HomeBottomRow.TranslatePoint(new Point(0, 0), HomeNews).Y;
        double bottom = HomePlaybackBar.IsVisible
            ? HomePlaybackBar.TranslatePoint(new Point(0, 0), HomeNews).Y - NewsGapAboveBar
            : rowTop - NewsGapAboveRow;
        Point version = VersionButton.TranslatePoint(new Point(VersionButton.ActualWidth / 2, 0), HomeNews);
        var layout = (Math.Round(bottom, 1), Math.Round(version.X, 1), Math.Round(version.Y + 4, 1));
        if (layout != _newsLastLayout)
        {
            _newsLastLayout = layout;
            HomeNews.SetLayout(layout.Item1, layout.Item2, layout.Item3, immediate: _newsPlaceImmediately);
            _newsPlaceImmediately = false;
        }
    }

    private void OnHomeNewsClick(object sender, RoutedEventArgs e) => HomeNewsPopup.IsOpen = !HomeNewsPopup.IsOpen;

    private void OnHomeNewsAllClick(object sender, RoutedEventArgs e)
    {
        HomeNewsPopup.IsOpen = false;
        ShowPlayerSettings(SettingsTab.Notifications);
    }

    /// <summary>Detalji obavijesti (iz kartice, popisa na Home ekranu ili Postavki); otvaranje je označava pročitanom.</summary>
    private void OpenNewsDetail(NewsItem item)
    {
        News.MarkRead(item.Id);
        _newsActed = true;
        if (HomeNews.Item?.Id == item.Id)
        {
            HomeNews.Hide();
        }

        _newsDetail?.Close();
        Window owner = _playerSettingsWindow is { IsLoaded: true } ? _playerSettingsWindow : this;
        var window = new NewsDetailWindow(item, RunNewsAction) { Owner = owner };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_newsDetail, window))
            {
                _newsDetail = null;
            }
        };
        _newsDetail = window;
        window.Show();
    }

    private void RunNewsAction(NewsItem item)
    {
        switch (item.Action)
        {
            case NewsAction.OpenAppearanceSettings:
                ShowPlayerSettings(SettingsTab.Appearance);
                break;
            case NewsAction.OpenAudioSettings:
                ShowPlayerSettings(SettingsTab.Audio);
                break;
            case NewsAction.OpenUpdates:
                ShowPlayerSettings(SettingsTab.Updates);
                break;
            case NewsAction.OpenNotifications:
                ShowPlayerSettings(SettingsTab.Notifications);
                break;
#if TORLINK
            case NewsAction.OpenTorlink:
                ShowPlayerSettings(SettingsTab.Torlink);
                break;
#endif
        }
    }
}
