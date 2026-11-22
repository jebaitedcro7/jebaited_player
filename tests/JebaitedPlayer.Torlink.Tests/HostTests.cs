using System.Net;

namespace JebaitedPlayer.Torlink.Tests;

[TestClass]
public class HostTests
{
    private sealed class Rig : IDisposable
    {
        public TempDir Dir = new();
        public FakeLauncher Launcher = new();
        public FakeClock Clock = new();
        public FakeFeed Feed = new();
        public TorlinkOptions Options = new();
        public string StatusJson = """{"downloads":[],"seeds":[],"network":{"online":true}}""";
        public FakeHandler Handler;
        public TorlinkHost Host;

        public Rig(bool installed = true, bool enabled = true)
        {
            if (installed)
            {
                TestRuntime.Install(Dir.Paths);
            }

            Handler = new FakeHandler(req => req.RequestUri!.AbsolutePath switch
            {
                "/health" => FakeHandler.Json("""{"ok":true,"version":"1.11.1"}"""),
                "/downloads" => FakeHandler.Json(StatusJson),
                "/add" => FakeHandler.Json("""{"ok":true}"""),
                "/control" => FakeHandler.Json("""{"ok":true}"""),
                _ => FakeHandler.Json("{}", HttpStatusCode.NotFound),
            });
            Host = new TorlinkHost(Dir.Paths, () => Options, Launcher, Feed, Clock, (port, token) => new ApiClient(port, token, Handler));
            Host.Supervisor.HealthProbe = (_, _) => Task.FromResult(true);
            Host.Enabled = enabled;
        }

        public void Dispose()
        {
            Host.Dispose();
            Dir.Dispose();
        }
    }

    [TestMethod]
    public void Status_ReflectsEnabledInstalledAndRunning()
    {
        using var r = new Rig(installed: false, enabled: false);
        Assert.AreEqual(TorlinkStatus.Disabled, r.Host.Status);
        r.Host.Enabled = true;
        Assert.AreEqual(TorlinkStatus.NotInstalled, r.Host.Status);
        TestRuntime.Install(r.Dir.Paths);
        Assert.AreEqual(TorlinkStatus.Ready, r.Host.Status);
    }

    [TestMethod]
    public async Task Disabled_NeverStartsAProcessOrSearches()
    {
        using var r = new Rig(enabled: false);
        Assert.IsFalse(await r.Host.EnsureRunningAsync());
        Assert.IsFalse(await r.Host.StartIfWorkPendingAsync());
        await Assert.ThrowsExceptionAsync<TorlinkSearchException>(() => r.Host.SearchAsync("x", null));
        await r.Host.TickAsync();
        Assert.AreEqual(0, r.Launcher.Started.Count);
        Assert.AreEqual(0, r.Launcher.StartInfos.Count);
    }

    [TestMethod]
    public async Task StartIfWorkPending_OnlyWhenQueueHasUnfinishedItems()
    {
        using var r = new Rig();
        Assert.IsFalse(await r.Host.StartIfWorkPendingAsync(), "nema posla: proces se ne diže");
        Directory.CreateDirectory(r.Dir.Paths.StateDataDir);
        File.WriteAllText(r.Dir.Paths.QueueFile, """[{"id":"a","status":"paused"}]""");
        Assert.IsFalse(await r.Host.StartIfWorkPendingAsync(), "pauzirano se ne nastavlja samo");
        File.WriteAllText(r.Dir.Paths.QueueFile, """[{"id":"a","status":"downloading"}]""");
        Assert.IsTrue(await r.Host.StartIfWorkPendingAsync());
        Assert.AreEqual(TorlinkStatus.Running, r.Host.Status);
    }

    [TestMethod]
    public async Task SeedingOff_StopsEverySeedThatStartsSharing()
    {
        using var r = new Rig();
        await r.Host.EnsureRunningAsync();
        r.StatusJson = """{"downloads":[],"seeds":[{"id":"abc","name":"N","status":"seeding","peers":2,"uploaded":10},{"id":"def","name":"M","status":"paused","peers":0,"uploaded":0}],"network":{"online":true}}""";
        await r.Host.RefreshAsync();
        var stops = r.Handler.Requests.Where(q => q.Path == "/control").ToList();
        Assert.AreEqual(1, stops.Count);
        StringAssert.Contains(stops[0].Body, "abc");
        StringAssert.Contains(stops[0].Body, "stop-seed");
    }

    [TestMethod]
    public async Task SeedingOn_LeavesSeedsAlone()
    {
        using var r = new Rig();
        r.Options = new TorlinkOptions { SeedAfter = true, SeedHours = 1 };
        await r.Host.EnsureRunningAsync();
        r.StatusJson = """{"downloads":[],"seeds":[{"id":"abc","name":"N","status":"seeding","peers":2,"uploaded":10}],"network":{"online":true}}""";
        await r.Host.RefreshAsync();
        Assert.AreEqual(0, r.Handler.Requests.Count(q => q.Path == "/control"));
    }

    [TestMethod]
    public async Task Idle_StopsProcessAfterTwoMinutes_ButNotWhileWorkOrPage()
    {
        using var r = new Rig();
        await r.Host.EnsureRunningAsync();
        r.Host.PageOpened();
        await r.Host.TickAsync();
        r.Clock.Advance(TimeSpan.FromMinutes(10));
        await r.Host.TickAsync();
        Assert.AreEqual(TorlinkStatus.Running, r.Host.Status, "stranica otvorena: ne gasi se");

        r.Host.PageClosed();
        r.StatusJson = """{"downloads":[{"id":"a","name":"N","status":"downloading","progress":10,"peers":1,"speed":1000}],"seeds":[],"network":{"online":true}}""";
        await r.Host.TickAsync();
        r.Clock.Advance(TimeSpan.FromMinutes(10));
        await r.Host.TickAsync();
        Assert.AreEqual(TorlinkStatus.Running, r.Host.Status, "preuzima se: ne gasi se");

        r.StatusJson = """{"downloads":[],"seeds":[],"network":{"online":true}}""";
        await r.Host.TickAsync();
        r.Clock.Advance(TimeSpan.FromMinutes(1));
        await r.Host.TickAsync();
        Assert.AreEqual(TorlinkStatus.Running, r.Host.Status, "mirovanje kraće od 2 min");
        r.Clock.Advance(TimeSpan.FromMinutes(2));
        await r.Host.TickAsync();
        Assert.AreEqual(TorlinkStatus.Ready, r.Host.Status, "nakon 2 min mirovanja proces je ugašen");
        Assert.IsTrue(r.Launcher.Started[0].Killed);
    }

    [TestMethod]
    public async Task AddResult_RefusesDuplicatesAlreadyDownloadedAndFullDisk()
    {
        using var r = new Rig();
        await r.Host.EnsureRunningAsync();
        Directory.CreateDirectory(r.Dir.Paths.StateDataDir);
        File.WriteAllText(r.Dir.Paths.HistoryFile, """[{"id":"11111111111111111111111111111111111111aa","name":"Done","sizeBytes":5,"dir":"D:/x","completedAt":1}]""");
        r.StatusJson = """{"downloads":[{"id":"22222222222222222222222222222222222222bb","name":"Active","status":"downloading","progress":1,"peers":0,"speed":0}],"seeds":[],"network":{"online":true}}""";
        await r.Host.RefreshAsync();

        TorrentResult Res(string hash, long size = 1000) => new(hash, "n", size, 1, 1, null, "yts", "magnet:?xt=urn:btih:" + hash, null);
        Assert.AreEqual(AddResult.AlreadyDownloaded, await r.Host.AddResultAsync(Res("11111111111111111111111111111111111111AA")));
        Assert.AreEqual(AddResult.Duplicate, await r.Host.AddResultAsync(Res("22222222222222222222222222222222222222bb")));
        Assert.AreEqual(0, r.Handler.Requests.Count(q => q.Path == "/add"), "duplikati ne idu Torlinku");

        Assert.AreEqual(AddResult.NoSpace, await r.Host.AddResultAsync(Res("33333333333333333333333333333333333333cc", long.MaxValue / 4)));
        Assert.IsFalse(r.Host.LastDiskCheck!.Enough);

        Assert.AreEqual(AddResult.Added, await r.Host.AddResultAsync(Res("44444444444444444444444444444444444444dd")));
        Assert.AreEqual(1, r.Handler.Requests.Count(q => q.Path == "/add"));
    }

    [TestMethod]
    public async Task AddInput_MagnetHashInvalidAndTorrentFile()
    {
        using var r = new Rig();
        Assert.AreEqual(AddResult.Added, await r.Host.AddInputAsync("magnet:?xt=urn:btih:08ada5a7a6183aae1e09d831df6748d566095a10&dn=x"));
        Assert.AreEqual(AddResult.Added, await r.Host.AddInputAsync("08ada5a7a6183aae1e09d831df6748d566095a10"));
        Assert.AreEqual(AddResult.Invalid, await r.Host.AddInputAsync("just words"));

        string torrent = Path.Combine(r.Dir.Path, "a.torrent");
        Directory.CreateDirectory(r.Dir.Path);
        File.WriteAllBytes(torrent, "d4:infod4:name1:aee"u8.ToArray());
        Assert.AreEqual(AddResult.Added, await r.Host.AddInputAsync("\"" + torrent + "\""));
        Assert.IsTrue(r.Handler.Requests.Any(q => q.Path == "/add" && q.Body.Contains("\"torrent\"")));

        string big = Path.Combine(r.Dir.Path, "big.torrent");
        File.WriteAllBytes(big, new byte[ApiClient.MaxTorrentBytes + 1]);
        Assert.AreEqual(AddResult.TooLarge, await r.Host.AddInputAsync(big));
    }

    [TestMethod]
    public async Task Add_WhenProcessCannotStart_ReportsNotRunning()
    {
        using var r = new Rig();
        r.Host.Supervisor.HealthProbe = (_, _) => Task.FromResult(false);
        r.Host.Supervisor.HealthTimeout = TimeSpan.FromMilliseconds(400);
        Assert.AreEqual(AddResult.NotRunning, await r.Host.AddInputAsync("08ada5a7a6183aae1e09d831df6748d566095a10"));
        Assert.AreEqual(TorlinkStatus.Failed, r.Host.Status);
    }

    [TestMethod]
    public async Task Control_RequiresRunningProcess_AndForwardsAction()
    {
        using var r = new Rig();
        Assert.IsFalse(await r.Host.ControlAsync("abc", "pause"));
        await r.Host.EnsureRunningAsync();
        Assert.IsTrue(await r.Host.ControlAsync("abc", "pause"));
        Assert.IsTrue(r.Handler.Requests.Any(q => q.Path == "/control" && q.Body.Contains("pause") && q.Body.Contains("abc")));
    }

    [TestMethod]
    public async Task Search_UsesRunnerAndReturnsParsedResults()
    {
        using var r = new Rig();
        r.Launcher.Run = _ => new ProcessResult(0, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "search.json")), string.Empty, false);
        SearchResultSet set = await r.Host.SearchAsync("big buck bunny", "movies");
        Assert.IsTrue(set.Results.Count > 0);
        System.Diagnostics.ProcessStartInfo psi = r.Launcher.StartInfos.Last();
        Assert.AreEqual("search", psi.ArgumentList[1]);
        Assert.AreEqual("big buck bunny", psi.ArgumentList[2]);
        Assert.IsFalse(psi.Environment.ContainsKey(TorlinkCommand.TokenVariable), "pretraga ne dobiva token");
    }

    [TestMethod]
    public async Task Search_Errors_AreTyped()
    {
        using var r = new Rig();
        await Assert.ThrowsExceptionAsync<TorlinkSearchException>(() => r.Host.SearchAsync("   ", null));
        r.Launcher.Run = _ => new ProcessResult(-1, "", "", TimedOut: true);
        TorlinkSearchException t = await Assert.ThrowsExceptionAsync<TorlinkSearchException>(() => r.Host.SearchAsync("x", null));
        Assert.AreEqual(SearchFailure.Timeout, t.Failure);
        r.Launcher.Run = _ => new ProcessResult(0, "garbage", "", false);
        TorlinkSearchException b = await Assert.ThrowsExceptionAsync<TorlinkSearchException>(() => r.Host.SearchAsync("x", null));
        Assert.AreEqual(SearchFailure.BadOutput, b.Failure);
    }

    [TestMethod]
    public async Task Shutdown_KillsProcess_AndStateSurvives()
    {
        using var r = new Rig();
        await r.Host.EnsureRunningAsync();
        Directory.CreateDirectory(r.Dir.Paths.StateDataDir);
        File.WriteAllText(r.Dir.Paths.QueueFile, """[{"id":"a","status":"downloading"}]""");
        await r.Host.ShutdownAsync();
        Assert.IsTrue(r.Launcher.Started[0].Killed);
        Assert.IsTrue(File.Exists(r.Dir.Paths.QueueFile), "red preuzimanja ostaje za nastavak pri sljedećem pokretanju");
        Assert.AreEqual(TorlinkStatus.Ready, r.Host.Status);
    }

    [TestMethod]
    public async Task Update_StopsProcess_InstallsAndRestartsIfItWasRunning()
    {
        using var r = new Rig();
        await r.Host.EnsureRunningAsync();
        string zip = TestRuntime.BuildZip(Path.Combine(r.Dir.Path, "src"), "1.12.0");
        string name = Path.GetFileName(zip);
        var rel = new RuntimeRelease("torlink-runtime-1.12.0-r1", new TorlinkVersion(1, 12, 0, 1), name, "https://github.com/z", 1, "https://github.com/s", "");
        r.Feed.Files[rel.ZipUrl] = zip;
        r.Feed.Texts[rel.SumsUrl] = $"{RuntimeStore.Sha256File(zip)}  {name}";
        UpdateResult u = await r.Host.UpdateAsync(rel, null);
        Assert.IsTrue(u.Success, u.Detail);
        Assert.AreEqual("1.12.0", r.Host.Installed!.TorlinkVersion);
        Assert.AreEqual(TorlinkStatus.Running, r.Host.Status, "nakon ažuriranja proces se ponovno diže");
        Assert.IsTrue(r.Host.Store.HasPrevious);
        Assert.IsTrue(await r.Host.RollbackAsync());
        Assert.AreEqual("1.11.1", r.Host.Installed!.TorlinkVersion);
    }

    [TestMethod]
    public async Task Update_FailedHealth_RestoresPreviousAndReportsIt()
    {
        using var r = new Rig();
        string zip = TestRuntime.BuildZip(Path.Combine(r.Dir.Path, "src"), "1.12.0");
        string name = Path.GetFileName(zip);
        var rel = new RuntimeRelease("torlink-runtime-1.12.0-r1", new TorlinkVersion(1, 12, 0, 1), name, "https://github.com/z", 1, "https://github.com/s", "");
        r.Feed.Files[rel.ZipUrl] = zip;
        r.Feed.Texts[rel.SumsUrl] = $"{RuntimeStore.Sha256File(zip)}  {name}";
        r.Host.Supervisor.HealthProbe = (_, _) => Task.FromResult(false);
        r.Host.Supervisor.HealthTimeout = TimeSpan.FromMilliseconds(400);
        UpdateResult u = await r.Host.UpdateAsync(rel, null);
        Assert.IsFalse(u.Success);
        Assert.IsTrue(u.RolledBack);
        Assert.AreEqual("1.11.1", r.Host.Installed!.TorlinkVersion);
    }
}
