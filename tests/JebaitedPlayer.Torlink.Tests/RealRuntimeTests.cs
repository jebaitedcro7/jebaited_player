using System.Diagnostics;

namespace JebaitedPlayer.Torlink.Tests;

/// <summary>
/// STVARNI testovi: pravi runtime paket (Node + Torlink), pravi procesi, prava mreža i legalni otvoreni filmovi Blender Foundation.
/// Pokreću se samo kad je postavljen TORLINK_TEST_RUNTIME_ZIP (zip iz tools/build-torlink-runtime.ps1); inače su preskočeni.
/// </summary>
[TestClass]
public class RealRuntimeTests
{
    private const string SintelHash = "08ada5a7a6183aae1e09d831df6748d566095a10";          // 129 MB, CC-BY Blender Foundation
    private const string BigBuckBunnyHash = "dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c";  // 276 MB, CC-BY Blender Foundation

    private static string ZipOrSkip()
    {
        string? zip = Environment.GetEnvironmentVariable("TORLINK_TEST_RUNTIME_ZIP");
        if (string.IsNullOrEmpty(zip) || !File.Exists(zip))
        {
            Assert.Inconclusive("TORLINK_TEST_RUNTIME_ZIP nije postavljen (pokreni tools/build-torlink-runtime.ps1).");
        }

        return zip!;
    }

    private sealed class Real : IDisposable
    {
        public TempDir Dir = new();
        public string Downloads;
        public TorlinkHost Host;

        public Real(string zip, TorlinkOptions? options = null)
        {
            Downloads = Path.Combine(Dir.Path, "dl");
            var store = new RuntimeStore(Dir.Paths);
            InstallResult r = store.InstallFromZip(zip, RuntimeStore.Sha256File(zip));
            Assert.IsTrue(r.Success, r.Failure + " " + r.Detail);
            TorlinkOptions o = options ?? new TorlinkOptions { DownloadDir = Downloads, MaxDownloads = 3 };
            Host = new TorlinkHost(Dir.Paths, () => o, new ProcessLauncher(), new GitHubReleaseFeed()) { Enabled = true };
        }

        public async Task<DownloadSnapshot> WaitSnapshot(Func<DownloadSnapshot, bool> cond, int seconds, string what)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            DownloadSnapshot s = DownloadSnapshot.Empty;
            while (DateTime.UtcNow < end)
            {
                s = await Host.RefreshAsync();
                if (cond(s))
                {
                    return s;
                }

                await Task.Delay(1000);
            }

            Assert.Fail($"Isteklo vrijeme ({seconds} s): {what}. Zadnje stanje: aktivna={s.Active.Count} red={s.Queued.Count} gotova={s.Completed.Count} neuspjela={s.Failed.Count}");
            return s;
        }

        public void Dispose()
        {
            Host.Dispose();
            Dir.Dispose();
        }
    }

    [TestMethod]
    public async Task Install_RealPackage_StartsHealthyProcessWithoutNodeDatachannel()
    {
        string zip = ZipOrSkip();
        using var r = new Real(zip);
        RuntimeManifest m = r.Host.Installed!;
        Assert.AreEqual("1.11.1", m.TorlinkVersion);
        Assert.IsTrue(m.Files.Count > 1000);
        Assert.IsFalse(Directory.Exists(Path.Combine(r.Dir.Paths.RuntimeDir, "torlink", "node_modules", "node-datachannel")));

        Assert.IsTrue(await r.Host.EnsureRunningAsync(), r.Host.Supervisor.LastError + " " + string.Join(" | ", r.Host.Supervisor.RecentOutput));
        Assert.AreEqual(TorlinkStatus.Running, r.Host.Status);
        string? version = await r.Host.Supervisor.Api!.HealthAsync();
        Assert.AreEqual("1.11.1", version);
        int pid = r.Host.Supervisor.ProcessId!.Value;
        Assert.AreEqual("node", Process.GetProcessById(pid).ProcessName.ToLowerInvariant());

        await r.Host.ShutdownAsync();
        await Task.Delay(500);
        Assert.IsTrue(IsGone(pid), "proces mora nestati nakon ShutdownAsync");
    }

    [TestMethod]
    public async Task Search_RealNetwork_ReturnsResultsAndPerSourceStatus()
    {
        string zip = ZipOrSkip();
        using var r = new Real(zip);
        SearchResultSet set = await r.Host.SearchAsync("Sintel", "movies");
        Assert.IsTrue(set.Sources.Count >= 2, "status po izvoru");
        Assert.IsTrue(set.Results.Count > 0, "rezultati za otvoreni film");
        Assert.IsTrue(set.Results.All(x => x.InfoHash.Length >= 32 && x.Magnet.StartsWith("magnet:")));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => r.Host.SearchAsync("Big Buck Bunny", "movies", cts.Token));
    }

    [TestMethod]
    public async Task Download_RealFlow_AddPauseResumeCompleteNoSeedDuplicateDelete()
    {
        string zip = ZipOrSkip();
        using var r = new Real(zip);

        Assert.AreEqual(AddResult.Added, await r.Host.AddInputAsync(SintelHash));
        DownloadSnapshot s = await r.WaitSnapshot(x => x.Active.Any(i => i.Id == SintelHash && i.Percent > 0), 90, "preuzimanje je krenulo");
        DownloadItem d = s.Active.Single(i => i.Id == SintelHash);
        Assert.AreEqual(DownloadState.Downloading, d.State);
        Assert.IsTrue(d.TotalBytes > 100_000_000, "ukupna veličina iz queue.json");
        Assert.IsTrue(d.Speed > 0);
        Assert.IsNotNull(d.EtaSeconds);
        Assert.AreEqual(AddResult.Duplicate, await r.Host.AddInputAsync(SintelHash), "isto preuzimanje se ne dodaje dvaput");

        Assert.IsTrue(await r.Host.ControlAsync(SintelHash, "pause"));
        s = await r.WaitSnapshot(x => x.Active.Any(i => i.Id == SintelHash && i.State == DownloadState.Paused), 20, "pauza");
        Assert.AreEqual(0, s.Active.Single().Speed);
        double pausedAt = s.Active.Single().Percent;
        await Task.Delay(3000);
        s = await r.Host.RefreshAsync();
        Assert.AreEqual(pausedAt, s.Active.Single().Percent, 1.0, "pauzirano preuzimanje ne napreduje");

        Assert.IsTrue(await r.Host.ControlAsync(SintelHash, "resume"));
        s = await r.WaitSnapshot(x => x.Completed.Any(i => i.Id == SintelHash), 300, "preuzimanje je završeno");
        Assert.IsTrue(Directory.EnumerateFiles(r.Downloads, "*", SearchOption.AllDirectories).Any(f => new FileInfo(f).Length > 100_000_000));

        await Task.Delay(3000);
        s = await r.Host.RefreshAsync();
        Assert.IsFalse(s.Completed.Single(i => i.Id == SintelHash).Seeding, "dijeljenje je zadano isključeno");
        Assert.AreEqual(AddResult.AlreadyDownloaded, await r.Host.AddInputAsync(SintelHash));

        Assert.IsTrue(await r.Host.ControlAsync(SintelHash, "delete"));
        await Task.Delay(1500);
        Assert.IsFalse(Directory.Exists(r.Downloads) && Directory.EnumerateFiles(r.Downloads, "*", SearchOption.AllDirectories).Any(f => new FileInfo(f).Length > 100_000_000), "delete briše i datoteke");
    }

    [TestMethod]
    public async Task Recovery_KillingTheProcessMidDownload_RestartsAndResumes()
    {
        string zip = ZipOrSkip();
        using var r = new Real(zip);
        Assert.AreEqual(AddResult.Added, await r.Host.AddInputAsync(BigBuckBunnyHash));
        DownloadSnapshot s = await r.WaitSnapshot(x => x.Active.Any(i => i.Percent >= 10), 120, "10 % preuzeto");
        double before = s.Active.Single().Percent;
        int pid = r.Host.Supervisor.ProcessId!.Value;

        Process.GetProcessById(pid).Kill(entireProcessTree: true);
        await Task.Delay(500);
        DateTime end = DateTime.UtcNow.AddSeconds(30);
        while (r.Host.Supervisor.State != SupervisorState.Running && DateTime.UtcNow < end || r.Host.Supervisor.ProcessId == pid)
        {
            await Task.Delay(250);
        }

        Assert.AreEqual(SupervisorState.Running, r.Host.Supervisor.State, "nadzornik je ponovno pokrenuo Torlink");
        Assert.AreNotEqual(pid, r.Host.Supervisor.ProcessId);

        s = await r.WaitSnapshot(x => x.Active.Any(i => i.State == DownloadState.Downloading && i.Percent >= before) || x.Completed.Count > 0, 90, "nastavak nakon pada");
        Assert.IsTrue(s.Active.Count == 0 || s.Active.Single().Percent >= before - 2, "napredak nije izgubljen");
        await r.Host.ControlAsync(BigBuckBunnyHash, "delete");
    }

    [TestMethod]
    public async Task Disable_WhileDownloading_PausesNothingLostAndNoProcessRemains()
    {
        string zip = ZipOrSkip();
        using var r = new Real(zip);
        await r.Host.AddInputAsync(BigBuckBunnyHash);
        await r.WaitSnapshot(x => x.Active.Any(i => i.Percent >= 3), 120, "preuzimanje je krenulo");
        int pid = r.Host.Supervisor.ProcessId!.Value;

        r.Host.Enabled = false;
        await r.Host.ShutdownAsync();
        await Task.Delay(500);
        Assert.IsTrue(IsGone(pid));
        Assert.AreEqual(TorlinkStatus.Disabled, r.Host.Status);
        Assert.IsTrue(File.Exists(r.Dir.Paths.QueueFile), "red ostaje na disku");

        // Ponovno uključivanje: Torlink sam nastavlja iz sačuvanog stanja.
        r.Host.Enabled = true;
        Assert.IsTrue(await r.Host.StartIfWorkPendingAsync());
        DownloadSnapshot s = await r.WaitSnapshot(x => x.Active.Any(i => i.State == DownloadState.Downloading), 60, "nastavak nakon ponovnog uključivanja");
        Assert.IsTrue(s.Active.Count == 1);
        await r.Host.ControlAsync(BigBuckBunnyHash, "delete");
    }

    [TestMethod]
    public async Task FirstInstall_ThroughUpdater_WithRealHealthCheck_CompletesEveryTime()
    {
        string zip = ZipOrSkip();
        string name = Path.GetFileName(zip);
        for (int i = 0; i < 4; i++)
        {
            using var t = new TempDir();
            var feed = new FakeFeed { Runtime = new RuntimeRelease("torlink-runtime-1.11.1-r1", new TorlinkVersion(1, 11, 1, 1), name, "https://github.com/z", 1, "https://github.com/s", "") };
            feed.Files["https://github.com/z"] = zip;
            feed.Texts["https://github.com/s"] = $"{RuntimeStore.Sha256File(zip)}  {name}";
            using var host = new TorlinkHost(t.Paths, () => new TorlinkOptions { DownloadDir = Path.Combine(t.Path, "dl") }, new ProcessLauncher(), feed) { Enabled = true };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Task<UpdateResult> task = host.UpdateAsync(feed.Runtime, null);
            Task done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.AreSame(task, done, $"UpdateAsync je zapeo (pokušaj {i + 1}), stanje: {host.Supervisor.State}, greška {host.Supervisor.LastError}");
            UpdateResult r = await task;
            Assert.IsTrue(r.Success, r.Step + " " + r.Detail);
            Assert.AreEqual("1.11.1", host.Installed!.TorlinkVersion);
            Assert.IsTrue(sw.Elapsed.TotalSeconds < 100, $"predugo: {sw.Elapsed.TotalSeconds:0} s");
        }
    }

    private static bool IsGone(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return p.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
