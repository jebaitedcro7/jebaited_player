using System.IO;
using JebaitedPlayer.Services.News;

namespace JebaitedPlayer.App.Tests;

/// <summary>Obavijesti (kartica na Home ekranu, tipka, Postavke): redoslijed, prikazivanje najviše 3 puta, odbačeno/pročitano, spremanje.</summary>
[TestClass]
public class NewsCenterTests
{
    private sealed class MemoryStore : INewsStateStore
    {
        public Dictionary<string, NewsState> Data { get; private set; } = [];

        public Dictionary<string, NewsState> Load() => Data.ToDictionary(p => p.Key, p => new NewsState { Status = p.Value.Status, ShownCount = p.Value.ShownCount });

        public void Save(Dictionary<string, NewsState> state) =>
            Data = state.ToDictionary(p => p.Key, p => new NewsState { Status = p.Value.Status, ShownCount = p.Value.ShownCount });
    }

    private static NewsItem Item(string id, NewsCategory category, DateTime? expires = null) =>
        new(id, category, "news.intro.title", "news.intro.body", "news.intro.detail", "news.intro.action", NewsAction.OpenNotifications, ExpiresUtc: expires);

    private static List<NewsItem> Catalog() =>
    [
        Item("tip", NewsCategory.Tip), Item("new1", NewsCategory.New), Item("imp", NewsCategory.Important), Item("new2", NewsCategory.New),
    ];

    [TestMethod]
    public void Items_are_ordered_important_update_new_tip_and_keep_catalog_order_inside_a_category()
    {
        var center = new NewsCenter(new MemoryStore(), Catalog());
        CollectionAssert.AreEqual(new[] { "imp", "new1", "new2", "tip" }, center.All.Select(i => i.Id).ToArray());
        Assert.AreEqual("imp", center.ActiveCard?.Id);
        Assert.AreEqual(4, center.UnreadCount);
    }

    [TestMethod]
    public void Dismissed_and_read_items_are_never_the_card_but_stay_in_the_list_with_their_state()
    {
        var center = new NewsCenter(new MemoryStore(), Catalog());
        center.Dismiss("imp");
        Assert.AreEqual("new1", center.ActiveCard?.Id);
        center.MarkRead("new1");
        Assert.AreEqual("new2", center.ActiveCard?.Id);
        Assert.AreEqual(NewsStatus.Dismissed, center.StatusOf("imp"));
        Assert.AreEqual(NewsStatus.Read, center.StatusOf("new1"));
        Assert.AreEqual(4, center.All.Count);
        Assert.AreEqual(2, center.UnreadCount);
    }

    [TestMethod]
    public void A_card_is_shown_at_most_three_times_counted_once_per_session()
    {
        var store = new MemoryStore();
        var catalog = new List<NewsItem> { Item("a", NewsCategory.New), Item("b", NewsCategory.Tip) };
        for (int session = 1; session <= 3; session++)
        {
            var center = new NewsCenter(store, catalog);
            Assert.AreEqual("a", center.ActiveCard?.Id, $"pokretanje {session}");
            center.NoteShown("a");
            center.NoteShown("a"); // isto pokretanje: ne računa se ponovno
            Assert.AreEqual("a", center.ActiveCard?.Id, "u istom pokretanju kartica ostaje prikazana");
        }

        var fourth = new NewsCenter(store, catalog);
        Assert.AreEqual(3, fourth.ShownCountOf("a"));
        Assert.AreEqual("b", fourth.ActiveCard?.Id, "nakon tri prikazivanja dolazi sljedeća");
        Assert.AreEqual(NewsStatus.Unread, fourth.StatusOf("a"), "i dalje je nepročitana u popisu");
        Assert.AreEqual(2, fourth.UnreadCount);
    }

    [TestMethod]
    public void State_survives_restart_and_mark_all_read_keeps_dismissed()
    {
        var store = new MemoryStore();
        var first = new NewsCenter(store, Catalog());
        first.Dismiss("tip");
        first.MarkAllRead();

        var second = new NewsCenter(store, Catalog());
        Assert.AreEqual(NewsStatus.Dismissed, second.StatusOf("tip"));
        Assert.AreEqual(NewsStatus.Read, second.StatusOf("new1"));
        Assert.AreEqual(0, second.UnreadCount);
        Assert.IsNull(second.ActiveCard);
    }

    [TestMethod]
    public void Update_notification_comes_only_from_the_update_service_version_and_goes_first()
    {
        var center = new NewsCenter(new MemoryStore(), Catalog());
        int changes = 0;
        center.Changed += (_, _) => changes++;

        center.SetUpdateVersion("2.3.15");
        NewsItem update = center.All[1]; // iza "Important", ispred "New"
        Assert.AreEqual("update-2.3.15", update.Id);
        Assert.AreEqual(NewsCategory.Update, update.Category);
        Assert.AreEqual(NewsAction.OpenUpdates, update.Action);
        Assert.AreEqual(1, changes);

        center.SetUpdateVersion("2.3.15"); // ista verzija: ništa se ne mijenja
        Assert.AreEqual(1, changes);

        center.SetUpdateVersion(null);
        Assert.IsFalse(center.All.Any(i => i.Id.StartsWith("update-", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Expired_items_disappear()
    {
        DateTime now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
        var catalog = new List<NewsItem> { Item("old", NewsCategory.New, now.AddDays(-1)), Item("live", NewsCategory.New, now.AddDays(1)), Item("forever", NewsCategory.Tip) };
        var center = new NewsCenter(new MemoryStore(), catalog, () => now);
        CollectionAssert.AreEqual(new[] { "live", "forever" }, center.All.Select(i => i.Id).ToArray());
    }

    [TestMethod]
    public void File_store_round_trips_readable_states_and_survives_a_corrupt_file()
    {
        string dir = Path.Combine(Path.GetTempPath(), "jp_news_" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(dir, "notifications.json");
            var store = new FileNewsStateStore(path);
            Assert.AreEqual(0, store.Load().Count, "nema datoteke");

            store.Save(new Dictionary<string, NewsState> { ["x"] = new() { Status = NewsStatus.Dismissed, ShownCount = 2 } });
            StringAssert.Contains(File.ReadAllText(path), "Dismissed", "stanje se piše čitljivo");
            NewsState loaded = store.Load()["x"];
            Assert.AreEqual(NewsStatus.Dismissed, loaded.Status);
            Assert.AreEqual(2, loaded.ShownCount);

            File.WriteAllText(path, "{ ovo nije json");
            Assert.AreEqual(0, store.Load().Count, "oštećena datoteka ne ruši player");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [TestMethod]
    public void Built_in_catalog_has_unique_ids_and_torlink_news_exists_only_in_the_github_build()
    {
        IReadOnlyList<NewsItem> items = NewsCatalog.BuiltIn;
        Assert.AreEqual(items.Count, items.Select(i => i.Id).Distinct().Count());
        Assert.IsTrue(items.All(i => i.Action == NewsAction.None || i.ActionKey.Length > 0));
        // Torlink tekstovi su u satelitskim datotekama (Store izdanje ih nema), pa samo Torlink obavijest smije koristiti taj prefiks.
        foreach (NewsItem item in items)
        {
            bool torlinkKeys = item.TitleKey.StartsWith("torlink.", StringComparison.Ordinal);
            Assert.AreEqual(item.Id.StartsWith("torlink", StringComparison.Ordinal), torlinkKeys, item.Id);
        }
    }

    [TestMethod]
    public void Every_category_and_status_has_a_translation()
    {
        foreach (NewsCategory category in Enum.GetValues<NewsCategory>())
        {
            string key = "news.cat." + category.ToString().ToLowerInvariant();
            Assert.AreNotEqual(key, JebaitedPlayer.Localization.Loc.T(key), key);
        }

        foreach (NewsStatus status in Enum.GetValues<NewsStatus>())
        {
            string key = "news.status." + status.ToString().ToLowerInvariant();
            Assert.AreNotEqual(key, JebaitedPlayer.Localization.Loc.T(key), key);
        }
    }
}
