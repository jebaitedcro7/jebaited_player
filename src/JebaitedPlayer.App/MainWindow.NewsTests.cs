using System.IO;
using System.Windows;
using System.Windows.Controls;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services.News;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>Dijagnostika obavijesti (<c>--diag-ui-test news</c> / <c>newsrestart</c>): stvarni prozori, miš i datoteka stanja.</summary>
public partial class MainWindow
{
    private Dictionary<string, object?> NewsGeometry()
    {
        Rect card = HomeNews.CardBounds;
        Rect bar = HomePlaybackBar.IsVisible
            ? new Rect(HomePlaybackBar.TranslatePoint(new Point(0, 0), HomeNews), new Size(HomePlaybackBar.ActualWidth, HomePlaybackBar.ActualHeight))
            : Rect.Empty;
        Point version = VersionButton.TranslatePoint(new Point(VersionButton.ActualWidth / 2, 0), HomeNews);
        Rect row = new(HomeBottomRow.TranslatePoint(new Point(0, 0), HomeNews), new Size(HomeBottomRow.ActualWidth, HomeBottomRow.ActualHeight));
        return new Dictionary<string, object?>
        {
            ["cardVisible"] = HomeNews.IsVisible && HomeNews.Item is not null, ["item"] = HomeNews.Item?.Id,
            ["cardTop"] = Math.Round(card.Top, 1), ["cardBottom"] = Math.Round(card.Bottom, 1), ["cardRight"] = Math.Round(card.Right, 1),
            ["overlayHeight"] = Math.Round(HomeNews.ActualHeight, 1), ["overlayWidth"] = Math.Round(HomeNews.ActualWidth, 1),
            ["barVisible"] = HomePlaybackBar.IsVisible, ["barTop"] = bar.IsEmpty ? null : Math.Round(bar.Top, 1),
            ["overlapsBar"] = !bar.IsEmpty && card.IntersectsWith(bar), ["rowTop"] = Math.Round(row.Top, 1),
            ["arrowVisible"] = HomeNews.ArrowVisible, ["arrowStraight"] = HomeNews.ArrowIsStraight,
            ["tipX"] = Math.Round(HomeNews.ArrowTip.X, 1), ["tipY"] = Math.Round(HomeNews.ArrowTip.Y, 1),
            ["versionX"] = Math.Round(version.X, 1), ["versionTop"] = Math.Round(version.Y, 1),
            ["badge"] = HomeNewsBadge.IsVisible ? HomeNewsBadgeText.Text : "", ["bellVisible"] = HomeNewsButton.IsVisible,
            ["bellLeft"] = Math.Round(HomeNewsButton.TranslatePoint(new Point(0, 0), HomeNews).X, 1),
            ["bellOverlapsToggle"] = HomeNewsButton.TranslatePoint(new Point(0, 0), HomeNews).X < HomeThemeSwitch.TranslatePoint(new Point(HomeThemeSwitch.ActualWidth, 0), HomeNews).X,
        };
    }

    private async Task<Dictionary<string, object?>> NewsUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 12000);
        string track = _vm.CurrentPath!;
        _player?.SetPropertyAsync("mute", "yes");
        r["start"] = new Dictionary<string, object?> { ["unread"] = News.UnreadCount, ["all"] = News.All.Select(i => i.Id).ToArray() };

        // 1) Bez playera: kartica NISKO, kratka ravna strelica.
        _vm.Stop();
        await WaitForAsync(() => !_vm.HasMedia && WelcomePanel.IsVisible, 5000);
        await Task.Delay(1500);
        r["low"] = NewsGeometry();
        r["lowScreenshot"] = SaveWindowPng("news_low");
        r["lowTitle"] = HomeNews.TitleForTests;

        // 2) Player se pojavi: kartica se glatko pomakne VISOKO iznad trake; strelica se produži i savije.
        OpenFiles([track]);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 10000);
        GoHome();
        await WaitForAsync(() => HomePlaybackBar.IsVisible, 4000);
        await Task.Delay(1300);
        var high = NewsGeometry();
        // Glatki pomak: isti SetLayout kao kad se traka pojavi; kartica se spusti 60 px i vrati, uzorkuje se usred pomaka.
        double highBottom = HomeNews.CardBounds.Bottom;
        HomeNews.SlideForTests(highBottom + 60);
        double mid = highBottom;
        for (int i = 0; i < 200 && Math.Abs(mid - highBottom) < 1; i++)
        {
            await Task.Delay(8);
            mid = HomeNews.CardBounds.Bottom;
        }

        await Task.Delay(500);
        HomeNews.SlideForTests(highBottom);
        await Task.Delay(500);
        high["midAnimationBottom"] = Math.Round(mid, 1);
        high["animationsEnabled"] = SystemParameters.ClientAreaAnimation;
        high["slideTarget"] = Math.Round(highBottom + 60, 1);
        high["restored"] = Math.Round(HomeNews.CardBounds.Bottom, 1);
        r["high"] = high;
        r["highScreenshot"] = SaveWindowPng("news_high");

        // 3) Player nestane: kartica se spusti natrag.
        _vm.Stop();
        await WaitForAsync(() => !_vm.HasMedia && !HomePlaybackBar.IsVisible, 5000);
        await Task.Delay(1000);
        r["lowAgain"] = NewsGeometry();

        // 4) Tipka Obavijesti: popis s brojem nepročitanih.
        ClickButton(HomeNewsButton);
        await Task.Delay(500);
        NewsListPanel popup = NewsPopupListForTests;
        r["popup"] = new Dictionary<string, object?> { ["open"] = NewsPopupOpen, ["rows"] = popup.RowCount, ["screenshot"] = SaveWindowPng("news_popup") };
        HomeNewsPopup.IsOpen = false;

        // 5) X na kartici: odbačeno, kartica nestaje, u istom pokretanju nema nove "nove funkcije/savjeta".
        string dismissedId = HomeNews.Item!.Id;
        ClickButton(HomeNews.CloseButtonForTests);
        await Task.Delay(1400);
        r["dismissed"] = new Dictionary<string, object?>
        {
            ["id"] = dismissedId, ["status"] = News.StatusOf(dismissedId).ToString(), ["cardVisible"] = HomeNews.IsVisible && HomeNews.Item is not null,
            ["badge"] = HomeNewsBadge.IsVisible ? HomeNewsBadgeText.Text : "", ["unread"] = News.UnreadCount,
        };

        // 6) Popis: klik na redak otvara detalje, označava pročitano, gumb radnje otvara pravu karticu Postavki.
        ClickButton(HomeNewsButton);
        await Task.Delay(400);
        string eqId = News.All.First(i => i.Action == NewsAction.OpenAudioSettings).Id;
        NewsPopupListForTests.ClickRowForTests(eqId);
        await WaitForAsync(() => _newsDetail is { IsLoaded: true }, 3000);
        await Task.Delay(300);
        r["detail"] = new Dictionary<string, object?>
        {
            ["open"] = _newsDetail is not null, ["title"] = _newsDetail?.TitleForTests, ["action"] = _newsDetail?.ActionVisibility.ToString(),
            ["status"] = News.StatusOf(eqId).ToString(), ["popupClosed"] = !NewsPopupOpen, ["screenshot"] = SaveWindowPng("news_detail", _newsDetail),
        };
        _newsDetail?.ClickActionForTests();
        await WaitForAsync(() => _playerSettingsWindow is { IsLoaded: true }, 5000);
        await Task.Delay(300);
        r["action"] = new Dictionary<string, object?> { ["tab"] = _playerSettingsWindow?.SelectedTab.ToString(), ["detailClosed"] = _newsDetail is null };

        // 7) Postavke → Obavijesti: isti popis, stanja, "Označi sve kao pročitano".
        ShowPlayerSettings(SettingsTab.Notifications);
        await Task.Delay(500);
        PlayerSettingsWindow sw = _playerSettingsWindow!;
        NewsListPanel list = sw.NewsListForTests!;
        var before = list.Rows.Select(x => x.Id + ":" + x.Status).ToArray();
        r["settings"] = new Dictionary<string, object?>
        {
            ["tab"] = sw.SelectedTab.ToString(), ["rows"] = before, ["screenshot"] = SaveWindowPng("news_settings", sw),
        };
        ClickButton((Button)sw.FindName("NewsMarkAllButton"));
        await Task.Delay(500);
        r["markAll"] = new Dictionary<string, object?>
        {
            ["rows"] = list.Rows.Select(x => x.Id + ":" + x.Status).ToArray(), ["unread"] = News.UnreadCount, ["badge"] = HomeNewsBadge.IsVisible,
        };
        sw.Close();
        await Task.Delay(300);

        // 8) Nova verzija iz UpdateService-a: pojavljuje se kao kartica i nakon reakcije (Update je važan), nestaje kad je nema.
        News.SetUpdateVersion("9.9.9");
        await Task.Delay(1500);
        var update = NewsGeometry();
        update["title"] = HomeNews.TitleForTests;
        r["update"] = update;
        News.SetUpdateVersion(null);
        await Task.Delay(1000);
        r["updateGone"] = !(HomeNews.IsVisible && HomeNews.Item is not null);

        // 9) Stanje je na disku (notifications.json), a ne u postavkama.
        string file = Path.Combine(AppPaths.DataDir, "notifications.json");
        string json = File.Exists(file) ? File.ReadAllText(file) : string.Empty;
        r["persist"] = new Dictionary<string, object?>
        {
            ["fileExists"] = json.Length > 0, ["hasDismissed"] = json.Contains(dismissedId) && json.Contains("Dismissed"),
            ["notInSettings"] = !File.Exists(AppPaths.SettingsFile) || !File.ReadAllText(AppPaths.SettingsFile).Contains("notifications", StringComparison.OrdinalIgnoreCase),
        };
        return r;
    }

    /// <summary>Drugo pokretanje s istom mapom podataka: pročitano i odbačeno se ne vraća, ništa se ne prikazuje.</summary>
    private async Task<Dictionary<string, object?>> NewsRestartUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 12000);
        _vm.Stop();
        await WaitForAsync(() => !_vm.HasMedia && WelcomePanel.IsVisible, 5000);
        await Task.Delay(2000);
        r["state"] = new Dictionary<string, object?>
        {
            ["cardVisible"] = HomeNews.IsVisible && HomeNews.Item is not null, ["badge"] = HomeNewsBadge.IsVisible,
            ["statuses"] = News.All.Select(i => i.Id + ":" + News.StatusOf(i.Id)).ToArray(),
            ["shown"] = News.All.Select(i => i.Id + ":" + News.ShownCountOf(i.Id)).ToArray(),
        };
        return r;
    }
}
