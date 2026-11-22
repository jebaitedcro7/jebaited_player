using System.Text.Json;

namespace JebaitedPlayer.Torlink.Tests;

[TestClass]
public class ParsingTests
{
    // Oblik odgovora snimljen iz stvarnog Torlinka 1.11.1 (sandbox test, Faza 1).
    private const string StatusJson = """
    {"downloads":[
      {"id":"aaaa","name":"Sintel","status":"downloading","progress":50,"peers":11,"speed":4000000},
      {"id":"bbbb","name":"Tears of Steel","status":"queued","progress":0,"peers":0,"speed":0},
      {"id":"cccc","name":"Paused one","status":"paused","progress":12,"peers":0,"speed":0},
      {"id":"dddd","name":"Broken","status":"failed","progress":7,"peers":0,"speed":0}],
     "seeds":[{"id":"eeee","name":"Cosmos","status":"seeding","peers":3,"uploaded":2048}],
     "network":{"interface":null,"address":null,"online":true,"peerPort":41084,"incoming":2}}
    """;

    [TestMethod]
    public void Status_ParsesDownloadsSeedsAndNetwork()
    {
        ApiStatus s = ApiStatus.Parse(StatusJson);
        Assert.AreEqual(4, s.Downloads.Count);
        Assert.AreEqual(50, s.Downloads[0].Progress);
        Assert.AreEqual("queued", s.Downloads[1].Status);
        Assert.AreEqual(1, s.Seeds.Count);
        Assert.AreEqual(2048, s.Seeds[0].Uploaded);
        Assert.AreEqual(41084, s.PeerPort);
        Assert.AreEqual(2, s.Incoming);
        Assert.IsTrue(s.NetworkOnline);
    }

    [TestMethod]
    public void Status_ToleratesMissingFields()
    {
        ApiStatus s = ApiStatus.Parse("""{"downloads":[{"id":"x"},{"name":"noid"}],"seeds":"oops"}""");
        Assert.AreEqual(1, s.Downloads.Count);
        Assert.AreEqual(0, s.Downloads[0].Progress);
        Assert.AreEqual(0, s.Seeds.Count);
    }

    [TestMethod]
    public void Snapshot_SplitsListsAndComputesTotalsAndEta()
    {
        var queue = new Dictionary<string, QueueEntry>
        {
            ["aaaa"] = new("aaaa", "downloading", 100_000_000, 0, @"D:\dl", null, 1),
            ["dddd"] = new("dddd", "failed", 50_000_000, 0, @"D:\dl", "no peers", 1),
        };
        var history = new List<HistoryEntry> { new("eeee", "Cosmos", 412_000_000, @"D:\dl", 1000) };
        DownloadSnapshot snap = DownloadSnapshot.Build(ApiStatus.Parse(StatusJson), queue, history);

        Assert.AreEqual(2, snap.Active.Count, "downloading + paused");
        Assert.AreEqual(1, snap.Queued.Count);
        Assert.AreEqual(1, snap.Failed.Count);
        Assert.AreEqual(1, snap.Completed.Count);
        Assert.IsTrue(snap.Completed[0].Seeding);

        DownloadItem d = snap.Active.Single(i => i.Id == "aaaa");
        Assert.AreEqual(50_000_000, d.DownloadedBytes, "preuzeto = % × ukupno");
        Assert.AreEqual(12.5, d.EtaSeconds!.Value, 0.01, "ETA = preostalo / brzina");
        Assert.AreEqual("no peers", snap.Failed[0].Error);
        Assert.IsTrue(snap.HasWork);
        Assert.AreEqual(4_000_000, snap.TotalSpeed);
        Assert.IsTrue(snap.IsCompleted("EEEE"));
        Assert.IsTrue(snap.Contains("bbbb"));
    }

    [TestMethod]
    public void Snapshot_NoEtaWithoutSpeedOrTotal()
    {
        DownloadSnapshot snap = DownloadSnapshot.Build(ApiStatus.Parse(StatusJson), new Dictionary<string, QueueEntry>(), []);
        Assert.IsNull(snap.Active.Single(i => i.Id == "aaaa").EtaSeconds);
        Assert.AreEqual("—", DownloadSnapshot.FormatEta(null));
    }

    [TestMethod]
    public void Format_Helpers()
    {
        Assert.AreEqual("~12 s", DownloadSnapshot.FormatEta(12));
        Assert.AreEqual("~3 min", DownloadSnapshot.FormatEta(180));
        Assert.AreEqual("129 MB", DownloadSnapshot.FormatSize(129_302_391));
        Assert.AreEqual("3.1 GB", DownloadSnapshot.FormatSize(3_100_000_000).Replace(',', '.'));
        Assert.AreEqual("4.1 MB/s", DownloadSnapshot.FormatSpeed(4_100_000).Replace(',', '.'));
    }

    [TestMethod]
    public void Search_ParsesRealOutputIncludingFailedSource()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "search.json"));
        SearchResultSet r = SearchResultSet.Parse(json);
        Assert.IsTrue(r.Results.Count >= 10);
        Assert.IsTrue(r.Results.All(x => x.Magnet.StartsWith("magnet:") && x.InfoHash.Length >= 32));
        Assert.IsTrue(r.Sources.Any(s => s.Id == "bittorrented" && !s.Ok && s.Code == "HTTP 401"));
        Assert.IsTrue(r.Sources.Any(s => s.Id == "tpb-movies" && s.Ok));
        Assert.IsFalse(r.AllFailed);
        Assert.AreEqual("TPB", r.Sources.First(s => s.Id == "tpb-movies").Label);
    }

    [TestMethod]
    public void Search_AllFailedWhenEverySourceFailed()
    {
        SearchResultSet r = SearchResultSet.Parse("""{"query":"x","category":"all","count":0,"sources":{"yts":{"ok":false,"count":0,"error":"e","code":"no response"}},"results":[]}""");
        Assert.IsTrue(r.AllFailed);
        Assert.AreEqual(0, r.Results.Count);
    }

    [TestMethod]
    public void Search_BadJsonThrows()
    {
        try
        {
            SearchResultSet.Parse("not json");
            Assert.Fail("očekivana JsonException");
        }
        catch (Exception ex)
        {
            Assert.IsInstanceOfType<JsonException>(ex);
        }
    }

    [TestMethod]
    public void SourceNames_HealthOnlyForSourcesThatReportIt()
    {
        Assert.IsFalse(SourceNames.ReportsHealth("fitgirl"));
        Assert.IsTrue(SourceNames.ReportsHealth("yts"));
    }

    [TestMethod]
    public void StateReader_ReadsQueueAndHistoryAndSurvivesGarbage()
    {
        using var t = new TempDir();
        TorlinkPaths p = t.Paths;
        Directory.CreateDirectory(p.StateDataDir);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "history.json"), p.HistoryFile);
        File.WriteAllText(p.QueueFile, """[{"id":"abc","name":"N","status":"downloading","progress":0,"totalBytes":276445467,"downloadedBytes":0,"dir":"D:/x","addedAt":5},{"nope":1}]""");
        var r = new StateReader(p);
        Assert.AreEqual(1, r.ReadQueue().Count);
        Assert.AreEqual(276445467, r.ReadQueue()["abc"].TotalBytes);
        Assert.IsTrue(r.HasResumableWork());
        Assert.AreEqual("Sintel", r.ReadHistory().Single().Name);

        File.WriteAllText(p.QueueFile, "{ broken");
        Assert.AreEqual(0, r.ReadQueue().Count, "pokvarena datoteka ne ruši čitanje");
        File.Delete(p.QueueFile);
        Assert.IsFalse(r.HasResumableWork());
    }
}
