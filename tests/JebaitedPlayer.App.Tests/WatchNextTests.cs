using System.IO;
using JebaitedPlayer.Library;
using JebaitedPlayer.ViewModels;
using Microsoft.Data.Sqlite;

namespace JebaitedPlayer.App.Tests;

/// <summary>Ciklus 5, N2: lista „Sljedeće za gledanje” (automatska sljedeća epizoda, ručno, ×, redoslijed).</summary>
[TestClass]
public sealed class WatchNextTests
{
    private string _dir = string.Empty;
    private WatchStore _store = null!;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "jp-watchnext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new WatchStore(Path.Combine(_dir, "library.db"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        _store.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Episode(string folder, string name)
    {
        string dir = Path.Combine(_dir, folder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[16]);
        return path;
    }

    /// <summary>Gledanje: zapis u povijesti (redom, zadnji je najnoviji) i pogledano ili započeto.</summary>
    private void Watch(string path, bool watched, double position = 1500)
    {
        Thread.Sleep(3);
        _store.RegisterPlay(path, null, null, null);
        _store.SavePosition(path, position, 1500, watched);
    }

    private IReadOnlyList<WatchNextEntry> Update() => LibraryController.UpdateWatchNext(_store);

    [TestMethod]
    public void Watched_episode_suggests_the_next_unwatched_one()
    {
        string e1 = Episode("Serija", "Serija.Test.S01E01.mkv");
        string e2 = Episode("Serija", "Serija.Test.S01E02.mkv");
        Episode("Serija", "Serija.Test.S01E03.mkv");
        Watch(e1, watched: true);

        IReadOnlyList<WatchNextEntry> list = Update();
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(e2, list[0].Path);
        Assert.IsTrue(list[0].IsAuto);
        StringAssert.Contains(list[0].Title, "S01E02");
    }

    [TestMethod]
    public void Next_episode_crosses_into_the_next_season()
    {
        string s1e2 = Episode(Path.Combine("Serija", "Season 01"), "Serija.Test.S01E02.mkv");
        string s2e1 = Episode(Path.Combine("Serija", "Season 02"), "Serija.Test.S02E01.mkv");
        Episode(Path.Combine("Serija", "Season 01"), "Serija.Test.S01E01.mkv");
        Watch(s1e2, watched: true);

        Assert.AreEqual(s2e1, Update().Single().Path);
    }

    [TestMethod]
    public void Started_or_watched_episodes_are_skipped_and_the_list_follows_the_series()
    {
        string e1 = Episode("S", "Serija.Test.S01E01.mkv");
        string e2 = Episode("S", "Serija.Test.S01E02.mkv");
        string e3 = Episode("S", "Serija.Test.S01E03.mkv");
        string e4 = Episode("S", "Serija.Test.S01E04.mkv");
        Watch(e2, watched: true);
        Watch(e1, watched: true);
        Assert.AreEqual(e3, Update().Single().Path, "E02 je već pogledana, pa ide E03.");

        // Započeta epizoda je u „Nastavi gledati”: automatska stavka nestaje.
        Watch(e3, watched: false, position: 300);
        Assert.AreEqual(0, Update().Count);

        // Pogledana E03: lista ide dalje (E04), stara stavka se ne vraća.
        Watch(e3, watched: true);
        Assert.AreEqual(e4, Update().Single().Path);
    }

    [TestMethod]
    public void Removed_auto_item_stays_removed_but_manual_add_brings_it_back()
    {
        string e1 = Episode("S", "Serija.Test.S01E01.mkv");
        string e2 = Episode("S", "Serija.Test.S01E02.mkv");
        Watch(e1, watched: true);
        Assert.AreEqual(1, Update().Count);

        _store.DismissWatchNext(e2);
        Assert.AreEqual(0, Update().Count, "Uklonjena automatska stavka se ne vraća.");

        Assert.IsTrue(_store.AddWatchNext(e2, "ručno", "manual"));
        WatchNextEntry back = Update().Single();
        Assert.IsFalse(back.IsAuto);
    }

    [TestMethod]
    public void Manual_items_keep_order_can_be_moved_and_disappear_when_watched()
    {
        string a = Episode("Filmovi", "Film.A.2020.mkv");
        string b = Episode("Filmovi", "Film.B.2021.mkv");
        string c = Episode("Filmovi", "Film.C.2022.mkv");
        foreach (string f in new[] { a, b, c })
        {
            Assert.IsTrue(_store.AddWatchNext(f, Path.GetFileName(f), "manual"));
        }

        Assert.IsFalse(_store.AddWatchNext(a, "A", "manual"), "Dvaput isto se ne dodaje.");
        _store.MoveWatchNext(c, -1);
        _store.MoveWatchNext(a, +1);
        CollectionAssert.AreEqual(new[] { c, a, b }, Update().Select(e => e.Path).ToArray());

        Watch(a, watched: true);
        CollectionAssert.AreEqual(new[] { c, b }, Update().Select(e => e.Path).ToArray());

        File.Delete(b);
        CollectionAssert.AreEqual(new[] { c }, Update().Select(e => e.Path).ToArray(), "Nedostupna datoteka se ne prikazuje.");
    }

    [TestMethod]
    public void Old_database_gets_the_new_table()
    {
        _store.Dispose();
        string db = Path.Combine(_dir, "library.db");
        using (var connection = new SqliteConnection($"Data Source={db};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "DROP TABLE watch_next; UPDATE meta SET value = '5' WHERE key = 'schema_version';";
            cmd.ExecuteNonQuery();
        }

        _store = new WatchStore(db);
        Assert.AreEqual(0, _store.WatchNext().Count);
        using var check = new SqliteConnection($"Data Source={db};Pooling=False");
        check.Open();
        using SqliteCommand version = check.CreateCommand();
        version.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
        Assert.AreEqual("6", version.ExecuteScalar());
    }
}
