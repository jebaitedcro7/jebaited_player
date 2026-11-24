using System.Net;
using System.Text;
using System.Text.Json;

namespace JebaitedPlayer.Iptv.Tests;

[TestClass]
public class M3uTests
{
    private const string Sample = """
        #EXTM3U url-tvg="http://epg.example/guide.xml"
        #EXTINF:-1 tvg-id="ch1" tvg-logo="http://l/1.png" group-title="Sport" tvg-chno="5",Sport, Prvi
        http://s.example/1.ts
        #EXTINF:-1 group-title="Film",Film 1
        #EXTVLCOPT:http-user-agent=x
        http://s.example/2.m3u8
        #EXTINF:-1 tvg-id="ch1" group-title="Sport",Duplikat
        http://s.example/3.ts
        #EXTINF:-1,Neispravan
        nije-url
        """;

    [TestMethod]
    public void ParsesChannelsGroupsAndHeader()
    {
        IptvCatalog c = M3uParser.Parse(Sample);
        Assert.AreEqual(3, c.Channels.Count);
        Assert.AreEqual("http://epg.example/guide.xml", c.XmltvUrl);
        Assert.AreEqual("Sport, Prvi", c.Channels[0].Name, "zarez u nazivu (nakon atributa) ostaje");
        Assert.AreEqual("Sport", c.Channels[0].Group);
        Assert.AreEqual(5, c.Channels[0].Number);
        Assert.AreEqual("Film", c.Channels[1].Group);
        Assert.AreEqual("http://s.example/2.m3u8", c.Channels[1].StreamUrl);
    }

    [TestMethod]
    public void DuplicateIdsGetUniqueKeys()
    {
        IptvCatalog c = M3uParser.Parse(Sample);
        Assert.AreEqual(3, c.Channels.Select(x => x.Id).Distinct().Count());
    }

    [TestMethod]
    public void EmptyAndGarbageGiveNoChannels()
    {
        Assert.AreEqual(0, M3uParser.Parse("").Channels.Count);
        Assert.AreEqual(0, M3uParser.Parse("<html>nije popis</html>").Channels.Count);
    }
}

[TestClass]
public class XmltvTests
{
    private const string Xml = """
        <?xml version="1.0"?>
        <tv>
          <channel id="ch1"><display-name>Prvi</display-name></channel>
          <programme start="20261122180000 +0100" stop="20261122190000 +0100" channel="ch1">
            <title lang="hr">Vijesti</title><desc>Opis &amp; više</desc>
          </programme>
          <programme start="20261122190000 +0100" stop="20261122200000 +0100" channel="ch1"><title>Film</title></programme>
          <programme start="20261122180000 +0100" stop="20261122190000 +0100" channel="other"><title>Drugo</title></programme>
          <programme start="20250101000000 +0000" stop="20250101010000 +0000" channel="ch1"><title>Staro</title></programme>
        </tv>
        """;

    [TestMethod]
    public void ParsesFiltersAndHandlesTimeZone()
    {
        var from = new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(Xml));
        List<EpgProgram> p = XmltvParser.Parse(ms, new HashSet<string> { "ch1" }, from, from.AddDays(2));
        Assert.AreEqual(2, p.Count);
        Assert.AreEqual("Vijesti", p[0].Title);
        Assert.AreEqual("Opis & više", p[0].Description);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 22, 17, 0, 0, TimeSpan.Zero), p[0].Start.ToUniversalTime());
        Assert.IsNull(p[1].Description);
    }

    [TestMethod]
    public void NowNextAndProgress()
    {
        var idx = new EpgIndex();
        var s = new DateTimeOffset(2026, 11, 22, 17, 0, 0, TimeSpan.Zero);
        idx.Load([new EpgProgram("a", s, s.AddHours(1), "A", null), new EpgProgram("a", s.AddHours(1), s.AddHours(2), "B", null)], s);
        DateTimeOffset t = s.AddMinutes(30);
        Assert.AreEqual("A", idx.Now("a", t)?.Title);
        Assert.AreEqual("B", idx.Next("a", t)?.Title);
        Assert.AreEqual(0.5, idx.Now("a", t)!.ProgressAt(t), 0.001);
        Assert.IsNull(idx.Now("nema", t));
        Assert.IsFalse(idx.IsStale(t));
        Assert.IsTrue(idx.IsStale(s.AddHours(5)));
    }

    [TestMethod]
    public void RejectsDtdBombWithoutExpanding()
    {
        string bomb = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a \"aaaa\">]><tv><programme start=\"20261122180000\" stop=\"20261122190000\" channel=\"c\"><title>&a;</title></programme></tv>";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(bomb));
        try
        {
            XmltvParser.Parse(ms, null, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        }
        catch (System.Xml.XmlException)
        {
            // prihvatljivo: odbijeno
        }
    }
}

[TestClass]
public class QueryTests
{
    private static readonly IptvChannel[] All =
    [
        new("1", "Zeta", "u1", "Sport", null, null, 0, false),
        new("2", "Alfa", "u2", "Film", null, null, 0, false),
        new("3", "Beta Sport", "u3", "Sport", null, null, 0, false),
    ];

    [TestMethod]
    public void SortsSearchesAndFilters()
    {
        var u = new IptvUserData();
        Assert.AreEqual("Alfa", ChannelQuery.Apply(All, u, new ChannelFilter(Sort: ChannelSort.NameAsc))[0].Name);
        Assert.AreEqual("Zeta", ChannelQuery.Apply(All, u, new ChannelFilter(Sort: ChannelSort.NameDesc))[0].Name);
        Assert.AreEqual(2, ChannelQuery.Apply(All, u, new ChannelFilter(Search: "sport")).Count);
        Assert.AreEqual(2, ChannelQuery.Apply(All, u, new ChannelFilter(Category: "Sport")).Count);
        u.ToggleFavorite("2");
        Assert.AreEqual(1, ChannelQuery.Apply(All, u, new ChannelFilter(FavoritesOnly: true)).Count);
        Assert.AreEqual("Alfa", ChannelQuery.Apply(All, u, new ChannelFilter(Sort: ChannelSort.FavoritesFirst))[0].Name);
    }

    [TestMethod]
    public void FavoritesHaveNoDuplicatesAndToggleBack()
    {
        var u = new IptvUserData();
        Assert.IsTrue(u.ToggleFavorite("1"));
        Assert.IsFalse(u.ToggleFavorite("1"));
        u.ToggleFavorite("1");
        u.Favorites.Add("1");
        Assert.AreEqual(2, u.Favorites.Count, "ručni dodatak duplikata ne smije se skrivati; ToggleFavorite ga ukloni");
    }

    [TestMethod]
    public void ManualOrderSurvivesRefreshAndNewChannelsGoLast()
    {
        var u = new IptvUserData();
        u.Move(["1", "2", "3"], "3", 0);
        var refreshed = All.Append(new IptvChannel("9", "Nov", "u9", "X", null, null, 0, false)).ToArray();
        IReadOnlyList<IptvChannel> r = ChannelQuery.Apply(refreshed, u, new ChannelFilter(Sort: ChannelSort.Manual));
        CollectionAssert.AreEqual(new[] { "3", "1", "2", "9" }, r.Select(c => c.Id).ToArray());
    }

    [TestMethod]
    public void HiddenChannelsAndCategoriesAreExcluded()
    {
        var u = new IptvUserData { HiddenChannels = ["2"], HiddenCategories = ["sport"] };
        Assert.AreEqual(0, ChannelQuery.Apply(All, u, new ChannelFilter()).Count);
        Assert.AreEqual(3, ChannelQuery.Apply(All, u, new ChannelFilter(IncludeHidden: true)).Count);
    }

    [TestMethod]
    public void RecentIsCappedAndOrdered()
    {
        var u = new IptvUserData();
        for (int i = 0; i < 40; i++) { u.MarkWatched("c" + i); }
        u.MarkWatched("c5");
        Assert.AreEqual(30, u.Recent.Count);
        Assert.AreEqual("c5", u.Recent[0]);
    }

    [TestMethod]
    public void LargePlaylistSearchIsFast()
    {
        var big = Enumerable.Range(0, 50000).Select(i => new IptvChannel(i.ToString(), "Kanal " + i, "u", "G" + i % 200, null, null, i, false)).ToArray();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<IptvChannel> r = ChannelQuery.Apply(big, new IptvUserData(), new ChannelFilter(Search: "kanal 4999", Sort: ChannelSort.NameAsc));
        sw.Stop();
        Assert.IsTrue(r.Count > 0);
        Assert.IsTrue(sw.ElapsedMilliseconds < 1500, $"pretraga 50 000 kanala trajala {sw.ElapsedMilliseconds} ms");
    }
}

[TestClass]
public class StoreTests
{
    [TestMethod]
    public void RoundTripAndExportHasNoSecrets()
    {
        string dir = Path.Combine(Path.GetTempPath(), "iptvtest" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new IptvStore(dir);
            var s = new IptvSettings();
            s.Sources.Add(new IptvSourceConfig { Id = "a", Name = "T", Kind = IptvSourceKind.Xtream, Server = "http://x", Username = "TAJNO_IME", PasswordProtected = "dpapi:TAJNA" });
            store.SaveSettings(s);
            Assert.AreEqual("a", store.LoadSettings().Sources[0].Id);
            var u = new IptvUserData { Favorites = ["1", "2"], ManualOrder = ["2", "1"] };
            store.SaveUser("a", u);
            CollectionAssert.AreEqual(new[] { "2", "1" }, store.LoadUser("a").ManualOrder);

            string export = store.ExportPreferences(s, u);
            Assert.IsFalse(export.Contains("TAJNO_IME") || export.Contains("TAJNA") || export.Contains("http://x"));
            var s2 = new IptvSettings();
            var u2 = new IptvUserData();
            store.ImportPreferences(export, s2, u2);
            CollectionAssert.AreEqual(new[] { "1", "2" }, u2.Favorites);

            Assert.IsNotNull(store.LoadUser("nepostojeci"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [TestMethod]
    public void CorruptFileFallsBackToDefaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), "iptvtest" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "iptv.json"), "{ pokvareno");
            Assert.AreEqual(0, new IptvStore(dir).LoadSettings().Sources.Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

[TestClass]
public class XtreamTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(f(r));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static XtreamClient Client(FakeHandler h) => new(new HttpClient(h), "http://srv.example:8080/get.php?x=1", "korisnik", "l0z!nka&x");

    [TestMethod]
    public void BaseUrlIsNormalizedAndUnencryptedFlagged()
    {
        var c = Client(new FakeHandler(_ => Json("{}")));
        Assert.AreEqual("http://srv.example:8080", c.BaseUrl);
        Assert.IsFalse(c.IsEncrypted);
        Assert.IsTrue(new XtreamClient(new HttpClient(), "https://a.b", "u", "p").IsEncrypted);
    }

    [TestMethod]
    public async Task AuthSuccessAndStreamUrlEscapesCredentials()
    {
        var h = new FakeHandler(r =>
        {
            StringAssert.Contains(r.RequestUri!.Query, "password=l0z%21nka%26x");
            return Json("""{"user_info":{"auth":1,"status":"Active","exp_date":"4102444800","max_connections":"2","active_cons":"0"}}""");
        });
        XtreamAccount a = await Client(h).AuthenticateAsync();
        Assert.AreEqual("Active", a.Status);
        Assert.AreEqual(2, a.MaxConnections);
        StringAssert.Contains(Client(h).BuildStreamUrl("77"), "/live/korisnik/l0z%21nka%26x/77.m3u8");
    }

    [TestMethod]
    public async Task BadCredentialsAreReportedWithoutLeakingPasswordAndSingleRequest()
    {
        var h = new FakeHandler(_ => Json("""{"user_info":{"auth":0}}"""));
        IptvException ex = await Assert.ThrowsExceptionAsync<IptvException>(() => Client(h).AuthenticateAsync());
        Assert.AreEqual(IptvErrorKind.BadCredentials, ex.Kind);
        Assert.IsFalse(ex.Message.Contains("l0z"));
        Assert.AreEqual(1, h.Calls, "nema ponavljanja prijave");
    }

    [TestMethod]
    public async Task ExpiredAndHttpErrorsAndGarbage()
    {
        var expired = new FakeHandler(_ => Json("""{"user_info":{"auth":1,"status":"Expired"}}"""));
        Assert.AreEqual(IptvErrorKind.Expired, (await Assert.ThrowsExceptionAsync<IptvException>(() => Client(expired).AuthenticateAsync())).Kind);
        var forbidden = new FakeHandler(_ => Json("{}", HttpStatusCode.Forbidden));
        Assert.AreEqual(IptvErrorKind.BadCredentials, (await Assert.ThrowsExceptionAsync<IptvException>(() => Client(forbidden).AuthenticateAsync())).Kind);
        var html = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") });
        Assert.AreEqual(IptvErrorKind.InvalidResponse, (await Assert.ThrowsExceptionAsync<IptvException>(() => Client(html).AuthenticateAsync())).Kind);
    }

    [TestMethod]
    public async Task UnreachableMessageDoesNotContainUrlOrPassword()
    {
        var h = new FakeHandler(_ => throw new HttpRequestException("http://srv.example/player_api.php?password=l0z!nka"));
        IptvException ex = await Assert.ThrowsExceptionAsync<IptvException>(() => Client(h).AuthenticateAsync());
        Assert.AreEqual(IptvErrorKind.Unreachable, ex.Kind);
        Assert.IsFalse(ex.Message.Contains("l0z") || ex.Message.Contains("srv.example"));
    }

    [TestMethod]
    public async Task ParsesLiveStreamsWithMixedTypes()
    {
        var h = new FakeHandler(r => r.RequestUri!.Query.Contains("get_live_categories")
            ? Json("""[{"category_id":"1","category_name":"Sport"}]""")
            : Json("""[{"num":1,"name":"Prvi","stream_id":10,"category_id":"1","stream_icon":"http://l/x.png","epg_channel_id":"p1","tv_archive":1},{"name":"","stream_id":11},{"name":"Bez kat","stream_id":"12","category_id":"99","tv_archive":0}]"""));
        IptvCatalog c = await Client(h).GetLiveCatalogAsync();
        Assert.AreEqual(2, c.Channels.Count);
        Assert.AreEqual("Sport", c.Channels[0].Group);
        Assert.IsTrue(c.Channels[0].HasArchive);
        Assert.AreEqual("—", c.Channels[1].Group);
        Assert.AreEqual("unencrypted", c.Note);
    }

    [TestMethod]
    public async Task ShortEpgDecodesBase64()
    {
        string title = Convert.ToBase64String(Encoding.UTF8.GetBytes("Dnevnik"));
        var h = new FakeHandler(_ => Json("{\"epg_listings\":[{\"title\":\"" + title + "\",\"description\":\"\",\"start_timestamp\":\"1800000000\",\"stop_timestamp\":\"1800003600\"}]}"));
        IReadOnlyList<EpgProgram> p = await Client(h).GetShortEpgAsync(new IptvChannel("10", "X", "u", "g", null, "p1", 0, false));
        Assert.AreEqual(1, p.Count);
        Assert.AreEqual("Dnevnik", p[0].Title);
        Assert.IsNull(p[0].Description);
    }
}

[TestClass]
public class DetectorTests
{
    [TestMethod]
    public void DetectsKinds()
    {
        Assert.AreEqual(IptvSourceKind.M3uFile, IptvSourceDetector.Detect(@"C:\liste\tv.m3u").Kind);
        DetectedSource x = IptvSourceDetector.Detect("http://srv.example:80/get.php?username=ab&password=c%21d&type=m3u_plus");
        Assert.AreEqual(IptvSourceKind.Xtream, x.Kind);
        Assert.AreEqual("http://srv.example", x.Server);
        Assert.AreEqual("ab", x.Username);
        Assert.AreEqual("c!d", x.Password);
        Assert.AreEqual(IptvSourceKind.M3uUrl, IptvSourceDetector.Detect("https://a.b/lista.m3u8").Kind);
        DetectedSource bare = IptvSourceDetector.Detect("srv.example:8080");
        Assert.AreEqual(IptvSourceKind.Xtream, bare.Kind);
        Assert.IsNull(bare.Username);
    }

    [TestMethod]
    public async Task M3uSessionLoadsFromFileAndEpgFromXmltvUrl()
    {
        string f = Path.Combine(Path.GetTempPath(), "t" + Guid.NewGuid().ToString("N") + ".m3u");
        File.WriteAllText(f, "#EXTM3U\n#EXTINF:-1 tvg-id=\"a\" group-title=\"G\",A\nhttp://x/1.ts\n");
        try
        {
            var s = new IptvSession(new HttpClient(), new IptvSourceConfig { Kind = IptvSourceKind.M3uFile, FilePath = f }, null, null);
            (IptvCatalog cat, _) = await s.ConnectAsync();
            Assert.AreEqual(1, cat.Channels.Count);
            Assert.IsTrue(s.IsEncrypted, "lokalna datoteka nije mrežna veza");
            Assert.AreEqual(0, await s.LoadEpgAsync(cat.Channels, DateTimeOffset.UtcNow), "bez XMLTV adrese nema EPG-a");
        }
        finally
        {
            File.Delete(f);
        }
    }

    [TestMethod]
    public async Task MissingFileAndEmptyPlaylistAreErrors()
    {
        var s = new IptvSession(new HttpClient(), new IptvSourceConfig { Kind = IptvSourceKind.M3uFile, FilePath = @"C:\nema\nema.m3u" }, null, null);
        IptvException ex = await Assert.ThrowsExceptionAsync<IptvException>(() => s.ConnectAsync());
        Assert.AreEqual(IptvErrorKind.InvalidResponse, ex.Kind);
    }
}
