namespace JebaitedPlayer.Library.Tests;

[TestClass]
public class MediaNameParserTests
{
    [TestMethod]
    public void Scene_episode_name_is_fully_parsed()
    {
        MediaInfo i = MediaNameParser.Parse(@"D:\TV\The.Office.S03E07.Branch.Wars.1080p.WEB-DL.x264-NTb.mkv");

        Assert.AreEqual("The Office", i.Title);
        Assert.AreEqual(3, i.Season);
        Assert.AreEqual(7, i.Episode);
        Assert.AreEqual("Branch Wars", i.EpisodeTitle);
        Assert.AreEqual("1080p", i.Resolution);
        Assert.AreEqual("WEB-DL", i.Source);
        Assert.AreEqual("H.264", i.VideoCodec);
        Assert.AreEqual("NTb", i.ReleaseGroup);
        Assert.AreEqual("S03E07", i.EpisodeCode);
        Assert.AreEqual("The Office · S03E07 · Branch Wars", i.DisplayTitle);
    }

    [TestMethod]
    public void Scene_movie_name_is_fully_parsed()
    {
        MediaInfo i = MediaNameParser.Parse(@"D:\Filmovi\Inception.2010.1080p.BluRay.x264-SPARKS.mkv");

        Assert.AreEqual("Inception", i.Title);
        Assert.AreEqual(2010, i.Year);
        Assert.IsFalse(i.IsEpisode);
        Assert.AreEqual("BluRay", i.Source);
        Assert.AreEqual("SPARKS", i.ReleaseGroup);
        Assert.AreEqual("Inception (2010)", i.DisplayTitle);
    }

    [TestMethod]
    [DataRow(@"D:\TV\Breaking Bad - 3x07 - One Minute.mkv", "Breaking Bad", 3, 7, "One Minute")]
    [DataRow(@"D:\TV\Show.Name.S01.E02.mkv", "Show Name", 1, 2, null)]
    [DataRow(@"D:\TV\Show.Name.2005.S01E01.Pilot.mkv", "Show Name", 1, 1, "Pilot")]
    [DataRow(@"D:\TV\the.mandalorian.s02e08.chapter.16.2160p.dsnp.web-dl.ddp5.1.atmos.hdr.hevc-mzabi.mkv", "the mandalorian", 2, 8, "chapter 16")]
    [DataRow(@"D:\Serije\Kum\Sezona 1 Epizoda 4.mkv", "Kum", 1, 4, null)]
    [DataRow(@"D:\TV\Serija\Season 2\Episode 05.mkv", "Serija", 2, 5, null)]
    [DataRow(@"D:\TV\Serija\S01E03.mkv", "Serija", 1, 3, null)]
    [DataRow(@"D:\TV\Serija\Season 01\E04.mkv", "Serija", 1, 4, null)]
    public void Episode_formats(string path, string title, int season, int episode, string? episodeTitle)
    {
        MediaInfo i = MediaNameParser.Parse(path);

        Assert.AreEqual(title, i.Title);
        Assert.AreEqual(season, i.Season);
        Assert.AreEqual(episode, i.Episode);
        Assert.AreEqual(episodeTitle, i.EpisodeTitle);
    }

    [TestMethod]
    [DataRow(@"D:\TV\Game.of.Thrones.S01E01E02.720p.HDTV.mkv", 1, 2)]
    [DataRow(@"D:\TV\Show.Name.S02E05-E06.mkv", 5, 6)]
    [DataRow(@"D:\TV\Show.Name.S02E05-06.mkv", 5, 6)]
    public void Multi_episode_files(string path, int first, int last)
    {
        MediaInfo i = MediaNameParser.Parse(path);

        Assert.AreEqual(first, i.Episode);
        Assert.AreEqual(last, i.EpisodeEnd);
    }

    [TestMethod]
    public void Anime_absolute_episode_and_group_in_brackets()
    {
        MediaInfo i = MediaNameParser.Parse(@"D:\Anime\Frieren\[SubsPlease] Frieren - 07 (1080p) [A1B2C3D4].mkv");

        Assert.AreEqual("Frieren", i.Title);
        Assert.AreEqual(7, i.Episode);
        Assert.IsNull(i.Season);
        Assert.AreEqual("1080p", i.Resolution);
        Assert.AreEqual("SubsPlease", i.ReleaseGroup);
        Assert.AreEqual("E07", i.EpisodeCode);
    }

    [TestMethod]
    [DataRow(@"D:\F\1917.2019.2160p.UHD.BluRay.x265.mkv", "1917", 2019, "2160p")]
    [DataRow(@"D:\F\Blade Runner 2049 (2017).mkv", "Blade Runner 2049", 2017, null)]
    [DataRow(@"D:\F\Movie.Name.2019.720p.WEBRip.x264.AAC-[YTS.MX].mp4", "Movie Name", 2019, "720p")]
    [DataRow(@"D:\F\Neki Film.avi", "Neki Film", null, null)]
    [DataRow(@"D:\F\WALL-E.2008.1080p.mkv", "WALL-E", 2008, "1080p")]
    public void Movie_names(string path, string title, int? year, string? resolution)
    {
        MediaInfo i = MediaNameParser.Parse(path);

        Assert.AreEqual(title, i.Title);
        Assert.AreEqual(year, i.Year);
        Assert.AreEqual(resolution, i.Resolution);
        Assert.IsFalse(i.IsEpisode, "Film ne smije biti prepoznat kao epizoda.");
    }

    [TestMethod]
    [DataRow("Movie.2010.1080p.WEB-DL.mkv", null)]
    [DataRow("Movie.2010.1080p.BluRay.x264-GRP.mkv", "GRP")]
    [DataRow("Movie.2010.1080p.BluRay.x264-GRP[rarbg].mkv", "GRP")]
    public void Release_group_ignores_quality_words(string name, string? group) =>
        Assert.AreEqual(group, MediaNameParser.Parse(@"D:\F\" + name).ReleaseGroup);
}

[TestClass]
public class NaturalSortTests
{
    [TestMethod]
    public void Numbers_are_sorted_by_value()
    {
        var list = new List<string> { "Epizoda 10.mkv", "Epizoda 2.mkv", "Epizoda 1.mkv", "epizoda 3.mkv", "Epizoda 02b.mkv" };

        list.Sort(NaturalStringComparer.Instance);

        CollectionAssert.AreEqual(new[] { "Epizoda 1.mkv", "Epizoda 2.mkv", "Epizoda 02b.mkv", "epizoda 3.mkv", "Epizoda 10.mkv" }, list);
    }
}

[TestClass]
public class HashAndScannerTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "jp-lib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    [TestMethod]
    [DataRow(200_000)]
    [DataRow(1000)]
    public void OpenSubtitles_hash_matches_reference_algorithm(int size)
    {
        string file = Path.Combine(_dir, "video.bin");
        byte[] data = new byte[size];
        new Random(7).NextBytes(data);
        File.WriteAllBytes(file, data);

        // Referentna izvedba: veličina + zbroj 64-bitnih riječi prvih i zadnjih 64 KB.
        ulong expected = (ulong)size;
        int chunk = Math.Min(65536, size);
        for (int i = 0; i + 8 <= chunk; i += 8)
        {
            expected += BitConverter.ToUInt64(data, i);
        }

        int tail = Math.Max(0, size - 65536);
        for (int i = 0; i + 8 <= chunk; i += 8)
        {
            expected += BitConverter.ToUInt64(data, tail + i);
        }

        Assert.AreEqual(expected.ToString("x16"), OpenSubtitlesHash.Compute(file));
    }

    [TestMethod]
    public void Folder_scan_orders_episodes_and_skips_samples()
    {
        string s1 = Path.Combine(_dir, "Serija", "Season 01");
        string s2 = Path.Combine(_dir, "Serija", "Season 02");
        string sample = Path.Combine(_dir, "Serija", "Sample");
        Directory.CreateDirectory(s1);
        Directory.CreateDirectory(s2);
        Directory.CreateDirectory(sample);
        foreach (string f in new[]
                 {
                     Path.Combine(s1, "Serija.S01E10.mkv"), Path.Combine(s1, "Serija.S01E02.mkv"), Path.Combine(s1, "Serija.S01E01.mkv"),
                     Path.Combine(s1, "serija.s01e01.sample.mkv"), Path.Combine(s2, "Serija.S02E01.mkv"),
                     Path.Combine(sample, "sample.mkv"), Path.Combine(_dir, "Serija", "notes.txt"),
                 })
        {
            File.WriteAllText(f, "x");
        }

        ScanResult result = MediaScanner.ScanFolder(_dir);

        CollectionAssert.AreEqual(
            new[] { "Serija.S01E01.mkv", "Serija.S01E02.mkv", "Serija.S01E10.mkv", "Serija.S02E01.mkv" },
            result.Files.Select(Path.GetFileName).ToArray());
        Assert.AreEqual(1, result.SkippedSamples);
    }
}

[TestClass]
public class WatchStoreTests
{
    private string _db = string.Empty;

    [TestInitialize]
    public void Setup() => _db = Path.Combine(Path.GetTempPath(), "jp-store-" + Guid.NewGuid().ToString("N"), "library.db");

    [TestCleanup]
    public void Cleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(Path.GetDirectoryName(_db)!, recursive: true);
    }

    [TestMethod]
    public void Position_history_and_subtitles_are_remembered_across_restarts()
    {
        using (var store = new WatchStore(_db))
        {
            store.RegisterPlay(@"D:\TV\A.S01E01.mkv", "A", 1, 1);
            store.SavePosition(@"D:\TV\A.S01E01.mkv", 2533.5, 3500, watched: false);
            store.RegisterPlay(@"D:\F\Film.mkv", "Film", null, null);
            store.SaveSubtitleState(@"D:\F\Film.mkv", new SubtitleState("external", @"D:\F\Film.hr.srt", null, "hr", -2000, 1.0427));
        }

        using (var store = new WatchStore(_db))
        {
            WatchEntry? e = store.Get(@"d:\tv\a.s01e01.mkv");
            Assert.IsNotNull(e, "Putanje se uspoređuju bez obzira na velika/mala slova.");
            Assert.AreEqual(2533.5, e.Position);
            Assert.AreEqual(3500, e.Duration);
            Assert.IsFalse(e.Watched);
            Assert.AreEqual(1, e.Season);

            var recent = store.Recent(10);
            Assert.AreEqual(2, recent.Count);

            SubtitleState? s = store.GetSubtitleState(@"D:\F\Film.mkv");
            Assert.IsNotNull(s);
            Assert.AreEqual("external", s.Choice);
            Assert.AreEqual(-2000, s.DelayMs);
            Assert.AreEqual(1.0427, s.Speed);

            store.Remove(@"D:\F\Film.mkv");
            Assert.AreEqual(1, store.Recent(10).Count);
        }
    }

    [TestMethod]
    public void Register_play_counts_views()
    {
        using var store = new WatchStore(_db);
        store.RegisterPlay(@"D:\F\Film.mkv", "Film", null, null);
        store.RegisterPlay(@"D:\F\Film.mkv", "Film", null, null);
        store.SetWatched(@"D:\F\Film.mkv", true);

        WatchEntry e = store.Get(@"D:\F\Film.mkv")!;
        Assert.AreEqual(2, e.PlayCount);
        Assert.IsTrue(e.Watched);
        Assert.AreEqual(0, e.Position);
    }

    [TestMethod]
    public void Favorites_are_remembered_newest_first_and_can_be_removed()
    {
        using (var store = new WatchStore(_db))
        {
            store.AddFavorite(@"D:\F\Film.mkv", "Film (2010)", "movie");
            Thread.Sleep(5);
            store.AddFavorite(@"D:\TV\Serija", "Serija", "series");
        }

        using (var store = new WatchStore(_db))
        {
            IReadOnlyList<Favorite> all = store.Favorites();
            CollectionAssert.AreEqual(new[] { "Serija", "Film (2010)" }, all.Select(f => f.Title).ToArray());
            Assert.IsTrue(all[0].IsSeries);
            Assert.IsTrue(store.IsFavorite(@"d:\f\film.mkv"), "Putanje se uspoređuju bez obzira na velika/mala slova.");

            store.RemoveFavorite(@"D:\F\Film.mkv");
            Assert.IsFalse(store.IsFavorite(@"D:\F\Film.mkv"));
            Assert.AreEqual(1, store.Favorites().Count);
            Assert.ThrowsException<ArgumentException>(() => store.AddFavorite(@"D:\x", "x", "nešto"));
        }
    }

    [TestMethod]
    public void Audio_track_choice_is_remembered_per_video()
    {
        using (var store = new WatchStore(_db))
        {
            store.SaveAudioState(@"D:\F\Film.mkv", new AudioState(2, "en"));
            store.SaveAudioState(@"D:\F\Film.mkv", new AudioState(3, "hr"));
        }

        using var again = new WatchStore(_db);
        Assert.AreEqual(new AudioState(3, "hr"), again.GetAudioState(@"D:\F\FILM.mkv"));
        Assert.IsNull(again.GetAudioState(@"D:\F\Drugi.mkv"));
    }

    [TestMethod]
    public void Database_from_m3_is_upgraded_without_losing_history()
    {
        // Baza kakvu je napravio M3 (shema 1, bez tablica za omiljene i audio).
        Directory.CreateDirectory(Path.GetDirectoryName(_db)!);
        using (var old = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db};Pooling=False"))
        {
            old.Open();
            using var cmd = old.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
                INSERT INTO meta VALUES ('schema_version', '1');
                CREATE TABLE media (path TEXT PRIMARY KEY COLLATE NOCASE, title TEXT, season INTEGER, episode INTEGER,
                    duration REAL NOT NULL DEFAULT 0, position REAL NOT NULL DEFAULT 0, watched INTEGER NOT NULL DEFAULT 0,
                    last_played TEXT, play_count INTEGER NOT NULL DEFAULT 0, file_size INTEGER, hash TEXT);
                INSERT INTO media (path, title, duration, position, last_played, play_count) VALUES ('D:\F\Stari.mkv', 'Stari', 100, 42, '2026-11-01T10:00:00.0000000Z', 1);
                """;
            cmd.ExecuteNonQuery();
        }

        using var store = new WatchStore(_db);
        Assert.AreEqual(42, store.Get(@"D:\F\Stari.mkv")!.Position);
        store.AddFavorite(@"D:\F\Stari.mkv", "Stari", "movie");
        Assert.AreEqual(1, store.Favorites().Count);
    }

    [TestMethod]
    public void Database_from_m6_keeps_subtitle_state_and_gains_time_map()
    {
        // Baza iz M6 (shema 2): zapamćeni titlovi bez korekcije po dijelovima.
        Directory.CreateDirectory(Path.GetDirectoryName(_db)!);
        using (var old = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db};Pooling=False"))
        {
            old.Open();
            using var cmd = old.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
                INSERT INTO meta VALUES ('schema_version', '2');
                CREATE TABLE subtitle_state (video_path TEXT PRIMARY KEY COLLATE NOCASE, choice TEXT NOT NULL, external_path TEXT,
                    embedded_id INTEGER, language TEXT, delay_ms REAL NOT NULL DEFAULT 0, speed REAL NOT NULL DEFAULT 1, updated TEXT);
                INSERT INTO subtitle_state (video_path, choice, external_path, language, delay_ms, speed)
                    VALUES ('D:\S\E05.mkv', 'external', 'D:\S\E05.hr.srt', 'hr', 3000, 1);
                """;
            cmd.ExecuteNonQuery();
        }

        using (var store = new WatchStore(_db))
        {
            Assert.AreEqual(new SubtitleState("external", @"D:\S\E05.hr.srt", null, "hr", 3000, 1), store.GetSubtitleState(@"D:\S\E05.mkv"));
            store.SaveSubtitleState(@"D:\S\E05.mkv", new SubtitleState("external", @"D:\S\E05.hr.srt", null, "hr", 0, 1, "0:1:600;463000:1:3780"));
        }

        using var again = new WatchStore(_db);
        Assert.AreEqual("0:1:600;463000:1:3780", again.GetSubtitleState(@"D:\S\E05.mkv")!.TimeMap);
    }
}
