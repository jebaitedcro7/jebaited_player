using JebaitedPlayer.Library;
using Microsoft.Data.Sqlite;

namespace JebaitedPlayer.Library.Tests;

/// <summary>Ciklus 1 (M6a, M6b): status „pogledano” se ne poništava, a broji se samo stvarno odgledano vrijeme.</summary>
[TestClass]
public sealed class WatchedTests
{
    private const string Video = @"D:\Serije\Show\Season 01\Show.S01E01.mkv";
    private string _db = string.Empty;

    [TestInitialize]
    public void Init() => _db = Path.Combine(Path.GetTempPath(), $"jp-watched-{Guid.NewGuid():N}.db");

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        foreach (string f in new[] { _db, _db + "-wal", _db + "-shm" })
        {
            try
            {
                File.Delete(f);
            }
            catch (IOException)
            {
            }
        }
    }

    // --- M6a ----------------------------------------------------------------------------------------------------

    [TestMethod]
    public void Reopening_a_watched_video_keeps_it_watched()
    {
        using var store = new WatchStore(_db);
        store.SavePosition(Video, 0, 2700, watched: true);

        // Ponovo otvoren i zatvoren na 10 %: prije 1.3.0 ovo je spremilo watched = 0 i zelena traka je nestala.
        store.SavePosition(Video, 270, 2700, watched: false);
        WatchEntry e = store.Get(Video)!;
        Assert.IsTrue(e.Watched);
        Assert.AreEqual(1, e.WatchedProgress, "Puna zelena traka ostaje.");
        Assert.AreEqual(270, e.Position);
    }

    [TestMethod]
    public void Only_manual_toggle_can_unwatch_and_it_clears_coverage()
    {
        using var store = new WatchStore(_db);
        var c = new WatchCoverage();
        c.AddPlayed(0, 2500, 2700);
        store.SavePosition(Video, 0, 2700, watched: true, c.ToText());

        store.SetWatched(Video, false);
        WatchEntry e = store.Get(Video)!;
        Assert.IsFalse(e.Watched);
        Assert.IsNull(e.Coverage, "Nepogledano počinje brojati ispočetka.");
        Assert.AreEqual(0, e.WatchedProgress);

        store.SetWatched(Video, true);
        Assert.IsTrue(store.Get(Video)!.Watched);
    }

    [TestMethod]
    public void Coverage_is_kept_when_a_save_has_none()
    {
        using var store = new WatchStore(_db);
        var c = new WatchCoverage();
        c.AddPlayed(0, 600, 2700);
        store.SavePosition(Video, 600, 2700, watched: false, c.ToText());
        store.SavePosition(Video, 650, 2700, watched: false);
        Assert.AreEqual(c.ToText(), store.Get(Video)!.Coverage);
    }

    [TestMethod]
    public void Database_from_1_2_gets_coverage_column_and_keeps_history()
    {
        // Baza kakvu stvara 1.2.0 (shema 3, bez stupca coverage).
        using (var conn = new SqliteConnection($"Data Source={_db};Pooling=False"))
        {
            conn.Open();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE media (path TEXT PRIMARY KEY COLLATE NOCASE, title TEXT, season INTEGER, episode INTEGER,
                    duration REAL NOT NULL DEFAULT 0, position REAL NOT NULL DEFAULT 0, watched INTEGER NOT NULL DEFAULT 0,
                    last_played TEXT, play_count INTEGER NOT NULL DEFAULT 0, file_size INTEGER, hash TEXT);
                INSERT INTO media (path, duration, position, watched, last_played) VALUES ('a.mkv', 3000, 0, 1, '2026-11-01T10:00:00.0000000Z');
                INSERT INTO media (path, duration, position, watched, last_played) VALUES ('b.mkv', 3000, 1500, 0, '2026-11-02T10:00:00.0000000Z');
                """;
            cmd.ExecuteNonQuery();
        }

        using var store = new WatchStore(_db);
        WatchEntry a = store.Get("a.mkv")!;
        WatchEntry b = store.Get("b.mkv")!;
        Assert.IsTrue(a.Watched);
        Assert.AreEqual(1, a.WatchedProgress);
        Assert.IsNull(b.Coverage);
        Assert.AreEqual(0.5, b.WatchedProgress, 1e-9, "Stari zapis pokazuje poziciju.");

        // Film započet u 1.2.0 i dogledan u 1.3.0 mora postati pogledan.
        WatchCoverage seeded = WatchCoverage.FromEntry(b);
        Assert.AreEqual(0.5, seeded.Fraction, 0.011);
        seeded.AddPlayed(1500, 2700, 3000);
        Assert.IsTrue(seeded.IsWatched);
    }

    // --- M6b ----------------------------------------------------------------------------------------------------

    [TestMethod]
    public void Seeking_to_the_end_is_not_watching()
    {
        var c = new WatchCoverage();
        c.AddPlayed(0, 60, 2700);          // prva minuta
        c.AddPlayed(2640, 2700, 2700);     // skok na kraj, zadnja minuta
        Assert.IsFalse(c.IsWatched);
        Assert.AreEqual(120.0 / 2700, c.Fraction, 0.011);
    }

    [TestMethod]
    public void Skimming_with_short_plays_between_jumps_is_not_watching()
    {
        // Strelica desno (+5 s) svake 0,3 s: puštano je samo 0,3 s od svakih 5 s.
        var c = new WatchCoverage();
        for (double t = 0; t < 2700; t += 5)
        {
            c.AddPlayed(t, t + 0.3, 2700);
        }

        Assert.IsTrue(c.Fraction < 0.1, $"pokriveno {c.Fraction:P1}");
        Assert.IsFalse(c.IsWatched);
    }

    [TestMethod]
    public void Watching_eighty_percent_counts_even_without_credits()
    {
        var c = new WatchCoverage();
        for (double t = 0; t < 2700 * 0.85; t += 0.2)
        {
            c.AddPlayed(t, t + 0.2, 2700);
        }

        Assert.IsTrue(c.IsWatched, "Odjavna špica se smije preskočiti.");
    }

    [TestMethod]
    public void Rewatching_the_same_part_does_not_add_up()
    {
        var c = new WatchCoverage();
        c.AddPlayed(0, 1080, 2700);
        c.AddPlayed(0, 1080, 2700);
        c.AddPlayed(0, 1080, 2700);
        Assert.AreEqual(0.4, c.Fraction, 0.011);
        Assert.IsFalse(c.IsWatched);
    }

    [TestMethod]
    public void Coverage_round_trips_through_text()
    {
        var c = new WatchCoverage();
        c.AddPlayed(100, 900, 1000);
        WatchCoverage back = WatchCoverage.Parse(c.ToText())!;
        Assert.AreEqual(c.Fraction, back.Fraction, 0.005);
        Assert.IsNull(WatchCoverage.Parse("ne-base64!"));
        Assert.IsNull(WatchCoverage.Parse(Convert.ToBase64String(new byte[7])));
        Assert.IsNull(WatchCoverage.Parse(null));
    }

    [TestMethod]
    public void Invalid_ranges_are_ignored()
    {
        var c = new WatchCoverage();
        c.AddPlayed(50, 40, 100);
        c.AddPlayed(0, 10, 0);
        c.AddPlayed(double.NaN, 10, 100);
        c.AddPlayed(0, double.PositiveInfinity, 100);
        Assert.AreEqual(0, c.Fraction);
        c.AddPlayed(-10, 1000, 100);
        Assert.AreEqual(1, c.Fraction, 1e-9);
    }
}
