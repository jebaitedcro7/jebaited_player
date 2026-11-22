using System.Net;
using System.Text;
using System.Text.Json;

namespace JebaitedPlayer.Torlink.Tests;

internal sealed class FakeFeed : IReleaseFeed
{
    public UpstreamRelease? Upstream { get; set; } = new("v1.12.0", "1.12.0", "Notes", "https://github.com/baairon/torlink/releases/tag/v1.12.0", null);
    public RuntimeRelease? Runtime { get; set; }
    public Dictionary<string, string> Texts { get; } = new();
    public Dictionary<string, string> Files { get; } = new();
    public bool FailUpstream { get; set; }
    public bool FailRuntime { get; set; }
    public bool FailDownload { get; set; }

    public Task<UpstreamRelease?> GetUpstreamLatestAsync(CancellationToken ct) =>
        FailUpstream ? throw new HttpRequestException("down") : Task.FromResult(Upstream);

    public Task<RuntimeRelease?> GetRuntimeLatestAsync(CancellationToken ct) =>
        FailRuntime ? throw new HttpRequestException("down") : Task.FromResult(Runtime);

    public Task<string> GetTextAsync(string url, CancellationToken ct) =>
        Texts.TryGetValue(url, out string? s) ? Task.FromResult(s) : throw new HttpRequestException("404");

    public Task DownloadAsync(string url, string destinationFile, long maxBytes, IProgress<double>? progress, CancellationToken ct)
    {
        if (FailDownload || !Files.TryGetValue(url, out string? src))
        {
            throw new HttpRequestException("download failed");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        File.Copy(src, destinationFile, true);
        progress?.Report(1);
        return Task.CompletedTask;
    }
}

[TestClass]
public class UpdaterTests
{
    private static (Updater Up, RuntimeStore Store, FakeFeed Feed, RuntimeRelease Rel) Setup(TempDir t, bool install = true)
    {
        if (install)
        {
            TestRuntime.Install(t.Paths, "1.11.1");
        }

        string zip = TestRuntime.BuildZip(Path.Combine(t.Path, "src"), "1.12.0");
        string zipName = Path.GetFileName(zip);
        var rel = new RuntimeRelease("torlink-runtime-1.12.0-r1", new TorlinkVersion(1, 12, 0, 1), zipName, "https://github.com/x/zip", 100, "https://github.com/x/sums", "n");
        var feed = new FakeFeed { Runtime = rel };
        feed.Files[rel.ZipUrl] = zip;
        feed.Texts[rel.SumsUrl] = $"{RuntimeStore.Sha256File(zip)}  {zipName}\n";
        var store = new RuntimeStore(t.Paths);
        return (new Updater(t.Paths, store, feed), store, feed, rel);
    }

    [TestMethod]
    public async Task Check_ReportsAvailableWhenNewerRuntimeExists()
    {
        using var t = new TempDir();
        var (up, _, _, _) = Setup(t);
        UpdateInfo i = await up.CheckAsync();
        Assert.IsTrue(i.Available);
        Assert.IsNull(i.Error);
        Assert.AreEqual("1.11.1", i.Installed!.TorlinkVersion);
        Assert.AreEqual("1.12.0", i.Upstream!.Version);
    }

    [TestMethod]
    public async Task Check_NotAvailableWhenSameOrOlder()
    {
        using var t = new TempDir();
        var (up, _, feed, _) = Setup(t);
        feed.Runtime = feed.Runtime! with { Version = new TorlinkVersion(1, 11, 1, 1) };
        Assert.IsFalse((await up.CheckAsync()).Available);
    }

    [TestMethod]
    public async Task Check_AvailableWhenNothingInstalled()
    {
        using var t = new TempDir();
        var (up, _, _, _) = Setup(t, install: false);
        Assert.IsTrue((await up.CheckAsync()).Available);
    }

    [TestMethod]
    public async Task Check_GitHubUnavailable_IsReportedNotThrown()
    {
        using var t = new TempDir();
        var (up, _, feed, _) = Setup(t);
        feed.FailUpstream = true;
        feed.FailRuntime = true;
        UpdateInfo i = await up.CheckAsync();
        Assert.AreEqual("both", i.Error);
        Assert.IsFalse(i.Available);
        feed.FailUpstream = false;
        Assert.AreEqual("runtime", (await up.CheckAsync()).Error);
    }

    [TestMethod]
    public async Task Check_UpstreamAheadOfRuntime_WhenOurPackageIsNotBuiltYet()
    {
        using var t = new TempDir();
        var (up, _, feed, _) = Setup(t);
        feed.Upstream = new UpstreamRelease("v1.13.0", "1.13.0", "", "", null);
        Assert.IsTrue((await up.CheckAsync()).UpstreamAheadOfRuntime);
    }

    [TestMethod]
    public async Task Install_Succeeds_ReportsEveryStep_AndKeepsBackup()
    {
        using var t = new TempDir();
        var (up, store, _, rel) = Setup(t);
        var steps = new List<UpdateStep>();
        bool stopped = false;
        UpdateResult r = await up.InstallAsync(rel, new SyncProgress<UpdateProgress>(p => steps.Add(p.Step)), () => { stopped = true; return Task.CompletedTask; }, () => Task.FromResult(true));
        Assert.IsTrue(r.Success, r.Detail);
        Assert.IsTrue(stopped, "procesi se gase prije zamjene datoteka");
        Assert.AreEqual("1.12.0", store.Installed()!.TorlinkVersion);
        Assert.AreEqual("1.11.1", store.PreviousManifest()!.TorlinkVersion);
        foreach (UpdateStep s in new[] { UpdateStep.Download, UpdateStep.Verify, UpdateStep.Backup, UpdateStep.HealthCheck, UpdateStep.Done })
        {
            CollectionAssert.Contains(steps, s);
        }

        Assert.IsFalse(File.Exists(Path.Combine(t.Paths.DownloadCache, rel.ZipName)), "preuzeti zip se briše");
    }

    [TestMethod]
    public async Task Install_FailedHealthCheck_RollsBackAutomatically()
    {
        using var t = new TempDir();
        var (up, store, _, rel) = Setup(t);
        var steps = new List<UpdateStep>();
        UpdateResult r = await up.InstallAsync(rel, new SyncProgress<UpdateProgress>(p => steps.Add(p.Step)), () => Task.CompletedTask, () => Task.FromResult(false));
        Assert.IsFalse(r.Success);
        Assert.IsTrue(r.RolledBack);
        Assert.AreEqual(UpdateStep.HealthCheck, r.Step);
        Assert.AreEqual("1.11.1", store.Installed()!.TorlinkVersion, "vraćena je prethodna ispravna verzija");
        CollectionAssert.Contains(steps, UpdateStep.RolledBack);
    }

    [TestMethod]
    public async Task FailedHealthOfSecondUpdate_RestoresCurrent_AndManualRollbackStillGoesToTheOlderOne()
    {
        using var t = new TempDir();
        var (up, store, feed, rel) = Setup(t);   // 1.11.1 instalirano, nudi se 1.12.0
        Assert.IsTrue((await up.InstallAsync(rel, null, () => Task.CompletedTask, () => Task.FromResult(true))).Success);
        Assert.AreEqual("1.12.0", store.Installed()!.TorlinkVersion);

        string zip3 = TestRuntime.BuildZip(Path.Combine(t.Path, "src3"), "1.13.0");
        string name3 = Path.GetFileName(zip3);
        var rel3 = new RuntimeRelease("torlink-runtime-1.13.0-r1", new TorlinkVersion(1, 13, 0, 1), name3, "https://github.com/z3", 1, "https://github.com/s3", "");
        feed.Files[rel3.ZipUrl] = zip3;
        feed.Texts[rel3.SumsUrl] = $"{RuntimeStore.Sha256File(zip3)}  {name3}";
        UpdateResult r = await up.InstallAsync(rel3, null, () => Task.CompletedTask, () => Task.FromResult(false));
        Assert.IsTrue(r.RolledBack);
        Assert.AreEqual("1.12.0", store.Installed()!.TorlinkVersion);
        Assert.AreEqual("1.11.1", store.PreviousManifest()!.TorlinkVersion, "neuspjela 1.13.0 ne smije postati 'prethodna'");

        Assert.IsTrue(store.Rollback());
        Assert.AreEqual("1.11.1", store.Installed()!.TorlinkVersion);
    }

    [TestMethod]
    public async Task Install_WrongChecksum_IsRejectedAndNothingChanges()
    {
        using var t = new TempDir();
        var (up, store, feed, rel) = Setup(t);
        feed.Texts[rel.SumsUrl] = new string('0', 64) + "  " + rel.ZipName + "\n";
        UpdateResult r = await up.InstallAsync(rel, null, () => Task.CompletedTask, () => Task.FromResult(true));
        Assert.IsFalse(r.Success);
        Assert.AreEqual(UpdateStep.Verify, r.Step);
        Assert.AreEqual("1.11.1", store.Installed()!.TorlinkVersion);
    }

    [TestMethod]
    public async Task Install_MissingChecksumFile_IsRejected()
    {
        using var t = new TempDir();
        var (up, _, feed, rel) = Setup(t);
        feed.Texts[rel.SumsUrl] = "# nothing here\n";
        UpdateResult r = await up.InstallAsync(rel, null, () => Task.CompletedTask, () => Task.FromResult(true));
        Assert.AreEqual("no-checksum", r.Detail);
    }

    [TestMethod]
    public async Task Install_DownloadFailure_IsReported_NoChange()
    {
        using var t = new TempDir();
        var (up, store, feed, rel) = Setup(t);
        feed.FailDownload = true;
        UpdateResult r = await up.InstallAsync(rel, null, () => Task.CompletedTask, () => Task.FromResult(true));
        Assert.IsFalse(r.Success);
        Assert.AreEqual(UpdateStep.Download, r.Step);
        Assert.AreEqual("1.11.1", store.Installed()!.TorlinkVersion);
    }

    [TestMethod]
    public void ParseSha256_HandlesBothFormats()
    {
        string h = new('a', 64);
        Assert.AreEqual(h, Updater.ParseSha256($"{h}  file.zip\r\n{new string('b', 64)} *other.zip", "file.zip"));
        Assert.AreEqual(new string('b', 64), Updater.ParseSha256($"{h}  file.zip\n{new string('b', 64)} *other.zip", "other.zip"));
        Assert.IsNull(Updater.ParseSha256($"{h}  file.zip", "missing.zip"));
        Assert.IsNull(Updater.ParseSha256($"{new string('z', 64)}  file.zip", "file.zip"));
    }

    [TestMethod]
    public void PickLatestRuntime_ChoosesHighestCompleteRelease()
    {
        const string json = """
        [
         {"tag_name":"torlink-runtime-1.11.1-r1","assets":[{"name":"torlink-runtime-1.11.1-r1-win-x64.zip","browser_download_url":"https://github.com/a","size":10},{"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/s1"}]},
         {"tag_name":"torlink-runtime-1.11.1-r2","assets":[{"name":"torlink-runtime-1.11.1-r2-win-x64.zip","browser_download_url":"https://github.com/b","size":11},{"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/s2"}]},
         {"tag_name":"torlink-runtime-1.12.0-r1","assets":[{"name":"torlink-runtime-1.12.0-r1-win-x64.zip","browser_download_url":"https://github.com/c","size":12}]},
         {"tag_name":"v2.1.13","assets":[]},
         {"tag_name":"libdave-1.2.2-openssl-3.6.5","assets":[]}
        ]
        """;
        using JsonDocument d = JsonDocument.Parse(json);
        RuntimeRelease? r = GitHubReleaseFeed.PickLatestRuntime(d.RootElement);
        Assert.AreEqual("torlink-runtime-1.11.1-r2", r!.Tag, "1.12.0 nema SHA256SUMS.txt pa se preskače; r2 > r1");
        Assert.AreEqual("https://github.com/s2", r.SumsUrl);
        Assert.IsNull(GitHubReleaseFeed.PickLatestRuntime(JsonDocument.Parse("{}").RootElement));
    }

    [TestMethod]
    public async Task GitHubFeed_RefusesNonGitHubOrNonHttpsAddresses()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("{}"));
        var feed = new GitHubReleaseFeed(handler);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => feed.GetTextAsync("https://evil.example.com/x", CancellationToken.None));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => feed.GetTextAsync("http://api.github.com/x", CancellationToken.None));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => feed.GetTextAsync("https://github.com.evil.com/x", CancellationToken.None));
        Assert.AreEqual(0, handler.Requests.Count, "zabranjene adrese se ni ne zovu");
        Assert.AreEqual("{}", await feed.GetTextAsync("https://api.github.com/x", CancellationToken.None));
    }

    [TestMethod]
    public async Task GitHubFeed_Download_WritesFile_AndRejectsOversize()
    {
        using var t = new TempDir();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[5000]) });
        var feed = new GitHubReleaseFeed(handler);
        string dest = Path.Combine(t.Path, "d", "a.zip");
        await feed.DownloadAsync("https://objects.githubusercontent.com/a.zip", dest, 10_000, null, CancellationToken.None);
        Assert.AreEqual(5000, new FileInfo(dest).Length);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => feed.DownloadAsync("https://objects.githubusercontent.com/a.zip", dest + "2", 1000, null, CancellationToken.None));
        Assert.IsFalse(File.Exists(dest + "2"));
    }

    [TestMethod]
    public async Task GitHubFeed_ParsesUpstreamLatest()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"tag_name":"v1.11.1","body":"Changes","html_url":"https://github.com/baairon/torlink/releases/tag/v1.11.1","published_at":"2026-11-16T22:48:58Z"}"""));
        UpstreamRelease? r = await new GitHubReleaseFeed(handler).GetUpstreamLatestAsync(CancellationToken.None);
        Assert.AreEqual("1.11.1", r!.Version);
        Assert.AreEqual("Changes", r.Notes);
        Assert.IsNotNull(r.Published);
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _a;

        public SyncProgress(Action<T> a) => _a = a;

        public void Report(T value) => _a(value);
    }
}
