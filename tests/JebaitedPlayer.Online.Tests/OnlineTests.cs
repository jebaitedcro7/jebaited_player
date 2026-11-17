using System.IO.Compression;
using System.Net;
using System.Text;
using JebaitedPlayer.Library;

namespace JebaitedPlayer.Online.Tests;

internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string?> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
        HttpResponseMessage response = respond(request);
        response.RequestMessage ??= request;
        return response;
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
}

internal static class TestData
{
    public static SubtitleQuery Movie(string file = @"D:\F\Inception.2010.1080p.BluRay.x264-SPARKS.mkv", string? hash = "8e245d9679d31e12") =>
        new(file, MediaNameParser.Parse(file), hash, 1000, 23.976, ["hr", "sr", "bs", "en"]);

    public static SubtitleQuery Episode() =>
        new(@"D:\TV\The.Office.S03E07.720p.WEB-DL.x264-NTb.mkv", MediaNameParser.Parse(@"D:\TV\The.Office.S03E07.720p.WEB-DL.x264-NTb.mkv"),
            null, 1000, null, ["hr", "en"]);

    public static SubtitleCandidate Candidate(string language, string? release = null, bool hash = false, int? season = null,
        int? episode = null, int? year = null, bool machine = false, string? title = null) => new()
    {
        Provider = "Test",
        DownloadReference = Guid.NewGuid().ToString("N"),
        Language = language,
        ReleaseName = release,
        HashMatch = hash,
        Season = season,
        Episode = episode,
        Year = year,
        MachineTranslated = machine,
        Title = title,
    };

    public static byte[] Zip(params (string Name, string Content)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name);
                using Stream s = entry.Open();
                byte[] b = Encoding.UTF8.GetBytes(content);
                s.Write(b, 0, b.Length);
            }
        }

        return ms.ToArray();
    }
}

[TestClass]
public class ScorerTests
{
    [TestMethod]
    public void Hash_match_is_sure()
    {
        SubtitleCandidate c = TestData.Candidate("hr", "Some.Other.Release", hash: true);

        Assert.IsTrue(SubtitleScorer.Score(c, TestData.Movie()));
        Assert.AreEqual(MatchConfidence.Sure, c.Confidence);
    }

    [TestMethod]
    public void Same_release_group_and_source_is_sure_without_hash()
    {
        SubtitleCandidate c = TestData.Candidate("hr", "Inception.2010.1080p.BluRay.x264-SPARKS");

        SubtitleScorer.Score(c, TestData.Movie(hash: null));

        Assert.AreEqual(MatchConfidence.Sure, c.Confidence);
        CollectionAssert.Contains(c.Reasons, "ista release grupa (SPARKS)");
    }

    [TestMethod]
    public void Same_film_different_release_is_only_likely_or_unsure()
    {
        SubtitleCandidate c = TestData.Candidate("hr", "Inception.2010.720p.WEBRip.x264-YTS");

        SubtitleScorer.Score(c, TestData.Movie(hash: null));

        Assert.AreNotEqual(MatchConfidence.Sure, c.Confidence);
    }

    [TestMethod]
    public void Different_episode_is_rejected()
    {
        Assert.IsFalse(SubtitleScorer.Score(TestData.Candidate("hr", "The.Office.S03E08.720p"), TestData.Episode()));
        Assert.IsFalse(SubtitleScorer.Score(TestData.Candidate("hr", null, season: 3, episode: 9), TestData.Episode()));
        Assert.IsTrue(SubtitleScorer.Score(TestData.Candidate("hr", "The.Office.S03E07.HDTV.x264-LOL"), TestData.Episode()));
    }

    [TestMethod]
    public void Wrong_year_title_and_machine_translation_are_penalized()
    {
        SubtitleQuery q = TestData.Movie(hash: null);
        SubtitleCandidate good = TestData.Candidate("hr", "Inception.2010.1080p.BluRay.x264-OTHER");
        SubtitleCandidate wrongYear = TestData.Candidate("hr", "Inception.1995.1080p.BluRay");
        SubtitleCandidate wrongTitle = TestData.Candidate("hr", "Interstellar.2014.1080p.BluRay");
        SubtitleCandidate machine = TestData.Candidate("hr", "Inception.2010.1080p.BluRay.x264-OTHER", machine: true);

        var sorted = SubtitleScorer.ScoreAndSort([wrongYear, machine, wrongTitle, good], q);

        Assert.AreSame(good, sorted[0]);
        Assert.IsTrue(wrongYear.Score < good.Score);
        Assert.IsTrue(wrongTitle.Score < good.Score);
        Assert.IsTrue(machine.Score < good.Score - 30);
    }

    [TestMethod]
    public void Release_name_of_another_show_is_penalized_even_with_matching_metadata()
    {
        // Kao u stvarnim rezultatima: automatski uvoz izvora upisao je ove titlove pod traženu seriju (metapodaci: naslov, S, E).
        string file = @"D:\TV\Night Watch S01\Night.Watch.S01E05.The.Long.Night.1080p.WEBRip.DDP5.1.H265-GRP.mkv";
        var q = new SubtitleQuery(file, MediaNameParser.Parse(file), null, 1000, null, ["hr", "sr", "bs", "en"]);
        SubtitleCandidate good = TestData.Candidate("hr", "Night Watch S01E05 480p", season: 1, episode: 5, title: "Night Watch");
        SubtitleCandidate dvd = TestData.Candidate("hr", "1x05 - The Long Night", season: 1, episode: 5, title: "Night Watch");
        SubtitleCandidate sequel = TestData.Candidate("bs", "Night.Watch.Sequel.S01E05.SWESUB.720p.WEB-DL.x264-FLM", season: 1, episode: 5,
            title: "Night Watch");
        SubtitleCandidate otherShow = TestData.Candidate("hr", "Daylight.2019.S01E05.720p.NF.WEBRip.x264-GLX", season: 1, episode: 5,
            title: "Night Watch");

        foreach (SubtitleCandidate c in new[] { good, dvd, sequel, otherShow })
        {
            Assert.IsTrue(SubtitleScorer.Score(c, q));
        }

        Assert.IsFalse(good.Reasons.Any(r => r.StartsWith("naziv izdanja", StringComparison.Ordinal)), string.Join("; ", good.Reasons));
        Assert.IsFalse(dvd.Reasons.Any(r => r.StartsWith("naziv izdanja", StringComparison.Ordinal)), string.Join("; ", dvd.Reasons));
        CollectionAssert.Contains(sequel.Reasons, "naziv izdanja je za drugi naslov (Night Watch Sequel)");
        CollectionAssert.Contains(otherShow.Reasons, "naziv izdanja je za drugi naslov (Daylight)");
        Assert.AreEqual(MatchConfidence.Unsure, otherShow.Confidence);
        Assert.IsTrue(otherShow.Score < good.Score - 40 && sequel.Score < good.Score - 40,
            $"dobar {good.Score} ({string.Join("; ", good.Reasons)}), druga serija {otherShow.Score} ({string.Join("; ", otherShow.Reasons)}), nastavak {sequel.Score} ({string.Join("; ", sequel.Reasons)})");
    }

    [TestMethod]
    public void Croatian_is_preferred_and_uncertain_croatian_is_offered_not_loaded()
    {
        SubtitleQuery q = TestData.Movie(hash: null);
        SubtitleCandidate hrUnsure = TestData.Candidate("hr", "Inception.2010.720p.WEBRip.x264-YTS");
        SubtitleCandidate enSure = TestData.Candidate("en", "Inception.2010.1080p.BluRay.x264-SPARKS");

        var sorted = SubtitleScorer.ScoreAndSort([enSure, hrUnsure], q);
        AutoDecision d = SubtitleScorer.Decide(sorted, q);

        Assert.AreSame(hrUnsure, sorted[0], "Hrvatski je prvi u popisu.");
        Assert.IsNull(d.AutoLoad, "Nesiguran hrvatski se ne učitava sam.");
        Assert.AreSame(hrUnsure, d.Best);
        Assert.AreEqual("hr", d.Language);
    }

    [TestMethod]
    public void Sure_croatian_is_loaded_automatically_and_missing_language_falls_through()
    {
        SubtitleQuery q = TestData.Movie();
        SubtitleCandidate srSure = TestData.Candidate("sr", "x", hash: true);
        var sorted = SubtitleScorer.ScoreAndSort([srSure], q);

        AutoDecision d = SubtitleScorer.Decide(sorted, q);

        Assert.AreSame(srSure, d.AutoLoad, "Nema hrvatskog, siguran srpski se učitava.");
        Assert.AreEqual("sr", d.Language);
    }
}

[TestClass]
public class ArchiveTests
{
    private static readonly MediaInfo Movie = MediaNameParser.Parse(@"D:\F\Film.2010.mkv");

    [TestMethod]
    public void Picks_matching_episode_from_season_pack()
    {
        byte[] zip = TestData.Zip(("Show.S01E01.srt", "a"), ("Show.S01E02.srt", "b"), ("Show.S01E03.srt", "c"), ("readme.txt", "x"));
        MediaInfo info = MediaNameParser.Parse(@"D:\TV\Show.S01E02.mkv");

        DownloadedSubtitle d = SafeSubtitleArchive.ExtractBest(zip, "x.srt", info);

        Assert.AreEqual("Show.S01E02.srt", d.FileName);
        Assert.AreEqual("b", Encoding.UTF8.GetString(d.Content));
    }

    [TestMethod]
    public void Paths_inside_archive_are_stripped()
    {
        byte[] zip = TestData.Zip(("../../Windows/evil.srt", "1\n00:00:01,000 --> 00:00:02,000\nx"));

        DownloadedSubtitle d = SafeSubtitleArchive.ExtractBest(zip, "x.srt", Movie);

        Assert.AreEqual("evil.srt", d.FileName);
    }

    [TestMethod]
    public void Rar_archives_and_archives_without_subtitles_are_rejected()
    {
        Assert.ThrowsException<ProviderException>(() => SafeSubtitleArchive.ExtractBest("Rar!\u001a\u0007\0"u8.ToArray(), "x.srt", Movie));
        Assert.ThrowsException<ProviderException>(() => SafeSubtitleArchive.ExtractBest(TestData.Zip(("setup.exe", "MZ"), ("a.txt", "x")), "x.srt", Movie));
    }

    [TestMethod]
    public void Zip_bomb_is_rejected()
    {
        byte[] zip = TestData.Zip(("bomb.srt", new string('a', 2_000_000)));

        Assert.ThrowsException<ProviderException>(() => SafeSubtitleArchive.ExtractBest(zip, "x.srt", Movie));
    }

    [TestMethod]
    public void Plain_subtitle_file_is_passed_through()
    {
        DownloadedSubtitle d = SafeSubtitleArchive.ExtractBest("1\n00:00:01,000 --> 00:00:02,000\nBok"u8.ToArray(), "film.hr.srt", Movie);

        Assert.AreEqual("film.hr.srt", d.FileName);
    }
}

[TestClass]
public class OpenSubtitlesTests
{
    private const string SearchJson = """
        {"total_count":2,"data":[
          {"id":"1","type":"subtitle","attributes":{"language":"hr","download_count":1234,"hearing_impaired":false,"fps":23.976,
            "from_trusted":true,"ai_translated":false,"machine_translated":false,"release":"Inception.2010.1080p.BluRay.x264-SPARKS",
            "uploader":{"name":"marko"},"moviehash_match":true,"url":"https://www.opensubtitles.com/hr/subtitles/x",
            "feature_details":{"year":2010,"title":"Inception","movie_name":"2010 - Inception","imdb_id":1375666},
            "files":[{"file_id":555,"cd_number":1,"file_name":"Inception.hr.srt"}]}},
          {"id":"2","type":"subtitle","attributes":{"language":"en","download_count":10,"release":"Inception.2010.720p",
            "machine_translated":true,"files":[{"file_id":777,"file_name":"x.srt"}]}}
        ]}
        """;

    [TestMethod]
    public void Search_sends_sorted_parameters_with_key_and_parses_results()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json(SearchJson));
        var provider = new OpenSubtitlesProvider(new HttpClient(handler), () => new OpenSubtitlesCredentials("KEY123", null, null));

        var results = provider.SearchAsync(TestData.Movie(), CancellationToken.None).Result;

        HttpRequestMessage request = handler.Requests.Single();
        Assert.AreEqual("https://api.opensubtitles.com/api/v1/subtitles?languages=bs,en,hr,sr&moviehash=8e245d9679d31e12&query=inception&type=movie&year=2010",
            request.RequestUri!.ToString().Replace("%2C", ","));
        Assert.AreEqual("KEY123", request.Headers.GetValues("Api-Key").Single());
        Assert.AreEqual(2, results.Count);
        Assert.IsTrue(results[0].HashMatch);
        Assert.AreEqual("555", results[0].DownloadReference);
        Assert.AreEqual(23.976, results[0].FrameRate);
        Assert.IsTrue(results[1].MachineTranslated);
    }

    [TestMethod]
    public void Episode_search_uses_season_and_episode_and_plus_for_spaces()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"data":[]}"""));
        var provider = new OpenSubtitlesProvider(new HttpClient(handler), () => new OpenSubtitlesCredentials("K", null, null));

        _ = provider.SearchAsync(TestData.Episode(), CancellationToken.None).Result;

        StringAssert.Contains(handler.Requests[0].RequestUri!.Query, "episode_number=7");
        StringAssert.Contains(handler.Requests[0].RequestUri!.Query, "query=the+office");
        StringAssert.Contains(handler.Requests[0].RequestUri!.Query, "season_number=3");
        StringAssert.Contains(handler.Requests[0].RequestUri!.Query, "type=episode");
    }

    [TestMethod]
    public void Download_gets_link_then_file()
    {
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath switch
        {
            "/api/v1/download" => FakeHandler.Json("""{"link":"https://www.opensubtitles.com/download/abc/file.srt","file_name":"Inception.hr.srt","remaining":4}"""),
            _ => FakeHandler.Bytes("1\n00:00:01,000 --> 00:00:02,000\nBok"u8.ToArray()),
        });
        var provider = new OpenSubtitlesProvider(new HttpClient(handler), () => new OpenSubtitlesCredentials("K", null, null));
        SubtitleCandidate c = TestData.Candidate("hr");
        c = new SubtitleCandidate { Provider = "OpenSubtitles", DownloadReference = "555", Language = "hr" };

        DownloadedSubtitle d = provider.DownloadAsync(c, TestData.Movie(), CancellationToken.None).Result;

        Assert.AreEqual("Inception.hr.srt", d.FileName);
        StringAssert.Contains(handler.Bodies[0], "\"file_id\":555");
    }

    [TestMethod]
    public void Download_limit_and_foreign_link_are_reported()
    {
        var limited = new OpenSubtitlesProvider(new HttpClient(new FakeHandler(_ =>
            FakeHandler.Json("""{"message":"limit","remaining":0,"reset_time":"7 sati"}""", HttpStatusCode.NotAcceptable))),
            () => new OpenSubtitlesCredentials("K", null, null));
        var c = new SubtitleCandidate { Provider = "OpenSubtitles", DownloadReference = "1", Language = "hr" };

        var ex = Assert.ThrowsException<AggregateException>(() => limited.DownloadAsync(c, TestData.Movie(), CancellationToken.None).Wait());
        StringAssert.Contains(ex.InnerException!.Message, "Dnevni limit");

        var foreign = new OpenSubtitlesProvider(new HttpClient(new FakeHandler(r => r.RequestUri!.AbsolutePath == "/api/v1/download"
                ? FakeHandler.Json("""{"link":"https://zlo.example.com/x.srt"}""")
                : FakeHandler.Bytes([1]))),
            () => new OpenSubtitlesCredentials("K", null, null));
        var ex2 = Assert.ThrowsException<AggregateException>(() => foreign.DownloadAsync(c, TestData.Movie(), CancellationToken.None).Wait());
        StringAssert.Contains(ex2.InnerException!.Message, "Odbijena");
    }

    [TestMethod]
    public void Bad_key_gives_clear_message()
    {
        var provider = new OpenSubtitlesProvider(new HttpClient(new FakeHandler(_ =>
            FakeHandler.Json("""{"message":"You cannot consume this service"}""", HttpStatusCode.Forbidden))),
            () => new OpenSubtitlesCredentials("BAD", null, null));

        var ex = Assert.ThrowsException<AggregateException>(() => provider.SearchAsync(TestData.Movie(), CancellationToken.None).Wait());

        Assert.AreEqual("OpenSubtitles je odbio API ključ.", ex.InnerException!.Message);
    }
}

[TestClass]
public class SubDlAndTitloviTests
{
    [TestMethod]
    public void SubDl_search_and_zip_download()
    {
        const string json = """
            {"status":true,"results":[{"sd_id":1,"type":"movie","name":"Inception","imdb_id":"tt1375666","year":2010}],
             "subtitles":[{"release_name":"Inception.2010.1080p.BluRay.x264-SPARKS","name":"SUBDL::x.zip","lang":"croatian","language":"HR",
               "author":"ana","url":"/subtitle/123-456.zip","subtitlePage":"/s/info/x","season":0,"episode":0,"hi":false,
               "releases":["Inception.2010.1080p.BluRay.x264-SPARKS","Inception.2010.720p.BluRay"]}]}
            """;
        byte[] zip = TestData.Zip(("Inception.hr.srt", "1\n00:00:01,000 --> 00:00:02,000\nBok"));
        var handler = new FakeHandler(r => r.RequestUri!.Host == "dl.subdl.com" ? FakeHandler.Bytes(zip) : FakeHandler.Json(json));
        var provider = new SubDlProvider(new HttpClient(handler), () => "KEY");

        var results = provider.SearchAsync(TestData.Movie(), CancellationToken.None).Result;
        DownloadedSubtitle d = provider.DownloadAsync(results[0], TestData.Movie(), CancellationToken.None).Result;

        StringAssert.Contains(handler.Requests[0].RequestUri!.Query, "languages=HR%2CSR%2CBS%2CEN");
        StringAssert.Contains(handler.Requests[0].RequestUri!.Query, "type=movie");
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("hr", results[0].Language);
        Assert.AreEqual(2, results[0].Releases.Count);
        Assert.AreEqual("https://dl.subdl.com/subtitle/123-456.zip", handler.Requests[1].RequestUri!.ToString());
        Assert.AreEqual("Inception.hr.srt", d.FileName);
    }

    [TestMethod]
    public void SubDl_api_key_is_not_kept_in_download_reference()
    {
        // SubDL u rezultatima vraća poveznicu s ključem; ključ ne smije završiti u predmemoriji ni u logu.
        const string json = """
            {"status":true,"results":[{"name":"Night Watch","year":2005}],
             "subtitles":[{"release_name":"Night Watch S1 E01-12","lang":"croatian","language":"HR",
               "url":"/subtitle/2081672-72525.zip?api_key=TAJNI_KLJUC","season":1,"episode":1}]}
            """;
        byte[] zip = TestData.Zip(("x.hr.srt", "1\n00:00:01,000 --> 00:00:02,000\nBok"));
        var handler = new FakeHandler(r => r.RequestUri!.Host == "dl.subdl.com" ? FakeHandler.Bytes(zip) : FakeHandler.Json(json));
        var provider = new SubDlProvider(new HttpClient(handler), () => "TAJNI_KLJUC");

        var results = provider.SearchAsync(TestData.Episode(), CancellationToken.None).Result;
        provider.DownloadAsync(results[0], TestData.Episode(), CancellationToken.None).Wait();

        Assert.AreEqual("/subtitle/2081672-72525.zip", results[0].DownloadReference);
        Assert.AreEqual("https://dl.subdl.com/subtitle/2081672-72525.zip", handler.Requests[1].RequestUri!.ToString());
        Assert.AreEqual("/a.zip?x=1", SubDlProvider.WithoutApiKey("/a.zip?api_key=K&x=1"));
    }

    [TestMethod]
    public void SubDl_download_adds_key_only_when_refused_without_it()
    {
        byte[] zip = TestData.Zip(("x.hr.srt", "1\n00:00:01,000 --> 00:00:02,000\nBok"));
        var handler = new FakeHandler(r => r.RequestUri!.Query.Contains("api_key=K")
            ? FakeHandler.Bytes(zip)
            : new HttpResponseMessage(HttpStatusCode.Forbidden));
        var provider = new SubDlProvider(new HttpClient(handler), () => "K");
        var candidate = new SubtitleCandidate { Provider = "SubDL", DownloadReference = "/subtitle/1.zip", Language = "hr" };

        DownloadedSubtitle d = provider.DownloadAsync(candidate, TestData.Episode(), CancellationToken.None).Result;

        Assert.AreEqual(2, handler.Requests.Count);
        Assert.AreEqual("x.hr.srt", d.FileName);
    }

    [TestMethod]
    public void SubDl_not_found_is_empty_not_error()
    {
        var provider = new SubDlProvider(new HttpClient(new FakeHandler(_ =>
            FakeHandler.Json("""{"status":false,"error":"can't find movie or tv"}"""))), () => "KEY");

        Assert.AreEqual(0, provider.SearchAsync(TestData.Movie(), CancellationToken.None).Result.Count);
    }

    [TestMethod]
    public void Titlovi_logs_in_searches_and_filters_episode()
    {
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath.EndsWith("/gettoken")
            ? FakeHandler.Json("""{"Token":"TOK","UserId":42,"ExpirationDate":"2099-01-01T00:00:00"}""")
            : FakeHandler.Json("""
                {"SubtitleResults":[
                  {"Id":1,"Title":"The Office","Lang":"Hrvatski","Link":"https://titlovi.com/download/?type=1&mediaid=1","Release":"HDTV.x264-LOL","Season":3,"Episode":7,"DownloadCount":50},
                  {"Id":2,"Title":"The Office","Lang":"Hrvatski","Link":"https://titlovi.com/download/?type=1&mediaid=2","Release":"","Season":3,"Episode":8},
                  {"Id":3,"Title":"The Office","Lang":"Bosanski","Link":"https://titlovi.com/download/?type=1&mediaid=3","Season":3,"Episode":7}
                ],"PagesAvailable":1}
                """));
        var provider = new TitloviProvider(new HttpClient(handler), () => new TitloviCredentials("user", "pass"));

        var results = provider.SearchAsync(TestData.Episode(), CancellationToken.None).Result;

        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        StringAssert.Contains(handler.Requests[1].RequestUri!.Query, "token=TOK");
        StringAssert.Contains(handler.Requests[1].RequestUri!.Query, "lang=hr%7Cen");
        Assert.AreEqual(2, results.Count, "S03E08 se izbacuje.");
        Assert.AreEqual("bs", results[1].Language);
    }
}

[TestClass]
public class SearchServiceTests
{
    [TestMethod]
    public void Failing_provider_does_not_stop_others_and_cache_is_used()
    {
        string cache = Path.Combine(Path.GetTempPath(), "jp-online-" + Guid.NewGuid().ToString("N"));
        try
        {
            var good = new CannedSubtitleProvider("Dobar", [TestData.Candidate("hr", "Inception.2010.1080p.BluRay.x264-SPARKS")],
                _ => new DownloadedSubtitle("a.srt", [1]));
            var bad = new SubDlProvider(new HttpClient(new FakeHandler(_ => FakeHandler.Json("{}", HttpStatusCode.Forbidden))), () => "K");
            var service = new SubtitleSearchService([good, bad], cache);

            SearchOutcome first = service.SearchAsync(TestData.Movie(hash: null), useCache: true, CancellationToken.None).Result;
            SearchOutcome second = service.SearchAsync(TestData.Movie(hash: null), useCache: true, CancellationToken.None).Result;

            Assert.AreEqual(1, first.Candidates.Count);
            Assert.IsFalse(first.Providers.Single(p => p.Provider == "SubDL").Ok);
            Assert.AreEqual("SubDL je odbio API ključ.", first.Providers.Single(p => p.Provider == "SubDL").Error);
            Assert.AreEqual(1, good.Searches, "Druga pretraga dolazi iz cachea.");
            Assert.IsTrue(second.Providers.Single(p => p.Provider == "Dobar").FromCache);
            Assert.AreEqual(MatchConfidence.Sure, second.Candidates[0].Confidence, "Rezultati iz cachea se ponovno boduju.");
        }
        finally
        {
            if (Directory.Exists(cache))
            {
                Directory.Delete(cache, true);
            }
        }
    }

    [TestMethod]
    public void Same_subtitle_is_downloaded_only_once()
    {
        // Prije se isti titl preuzimao i 6 puta (provjera prema govoru + ručni odabir), pa je dnevni limit brzo potrošen.
        string cache = Path.Combine(Path.GetTempPath(), "jp-online-" + Guid.NewGuid().ToString("N"));
        try
        {
            SubtitleCandidate c = TestData.Candidate("hr", "Inception.2010.1080p.BluRay.x264-SPARKS");
            var provider = new CannedSubtitleProvider("Test", [c], _ => new DownloadedSubtitle("a.hr.srt", [1, 2, 3]));
            var service = new SubtitleSearchService([provider], null, downloadCacheDir: cache);

            DownloadedSubtitle first = service.DownloadAsync(c, TestData.Movie(), CancellationToken.None).Result;
            DownloadedSubtitle second = service.DownloadAsync(c, TestData.Movie(), CancellationToken.None).Result;

            Assert.AreEqual(1, provider.Downloads);
            Assert.IsTrue(service.LastDownloadFromCache);
            Assert.AreEqual("a.hr.srt", second.FileName);
            CollectionAssert.AreEqual(first.Content, second.Content);
        }
        finally
        {
            if (Directory.Exists(cache))
            {
                Directory.Delete(cache, true);
            }
        }
    }

    [TestMethod]
    public void Old_search_cache_with_api_key_is_deleted()
    {
        string cache = Path.Combine(Path.GetTempPath(), "jp-online-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new CannedSubtitleProvider("SubDL", [TestData.Candidate("hr", "Inception.2010.1080p.BluRay.x264-SPARKS")],
                _ => new DownloadedSubtitle("a.srt", [1]));
            var service = new SubtitleSearchService([provider], cache);
            service.SearchAsync(TestData.Movie(hash: null), useCache: true, CancellationToken.None).Wait();
            string file = Directory.GetFiles(cache).Single();
            File.WriteAllText(file, File.ReadAllText(file).Replace("\"DownloadReference\":\"", "\"DownloadReference\":\"/x.zip?api_key=K&"));

            service.SearchAsync(TestData.Movie(hash: null), useCache: true, CancellationToken.None).Wait();

            Assert.AreEqual(2, provider.Searches, "Stara datoteka s ključem se ne koristi.");
            Assert.IsFalse(File.ReadAllText(Directory.GetFiles(cache).Single()).Contains("api_key"));
        }
        finally
        {
            if (Directory.Exists(cache))
            {
                Directory.Delete(cache, true);
            }
        }
    }
}
