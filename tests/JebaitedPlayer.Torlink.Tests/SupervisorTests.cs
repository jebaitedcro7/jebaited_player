namespace JebaitedPlayer.Torlink.Tests;

[TestClass]
public class SupervisorTests
{
    private static Supervisor Make(TempDir t, FakeLauncher l, FakeClock? clock = null, bool runtime = true)
    {
        if (runtime)
        {
            TestRuntime.Install(t.Paths);
        }

        return new Supervisor(t.Paths, l, clock) { HealthProbe = (_, _) => Task.FromResult(true) };
    }

    [TestMethod]
    public async Task Start_LaunchesOneProcessWithTokenOnlyInEnvironment()
    {
        using var t = new TempDir();
        var l = new FakeLauncher();
        using Supervisor s = Make(t, l);
        Assert.IsTrue(await s.StartAsync(new TorlinkOptions()));
        Assert.AreEqual(SupervisorState.Running, s.State);
        Assert.AreEqual(1, l.Started.Count);

        System.Diagnostics.ProcessStartInfo psi = l.StartInfos[0];
        string token = psi.Environment[TorlinkCommand.TokenVariable]!;
        Assert.AreEqual(64, token.Length);
        Assert.IsFalse(psi.ArgumentList.Any(a => a.Contains(token)));
        Assert.IsTrue(File.Exists(t.Paths.ConfigFile), "config.json je zapisan prije pokretanja");
        Assert.IsTrue(File.Exists(t.Paths.PidFile));

        Assert.IsTrue(await s.StartAsync(new TorlinkOptions()), "drugi poziv ne pokreće drugi proces");
        Assert.AreEqual(1, l.Started.Count);
    }

    [TestMethod]
    public async Task Start_FailsCleanlyWithoutRuntime()
    {
        using var t = new TempDir();
        var l = new FakeLauncher();
        using Supervisor s = Make(t, l, runtime: false);
        Assert.IsFalse(await s.StartAsync(new TorlinkOptions()));
        Assert.AreEqual(SupervisorState.Failed, s.State);
        Assert.AreEqual("runtime-missing", s.LastError);
        Assert.AreEqual(0, l.Started.Count);
    }

    [TestMethod]
    public async Task Stop_KillsProcessAndRemovesPidFile()
    {
        using var t = new TempDir();
        var l = new FakeLauncher();
        using Supervisor s = Make(t, l);
        await s.StartAsync(new TorlinkOptions());
        await s.StopAsync();
        Assert.IsTrue(l.Started[0].Killed);
        Assert.AreEqual(SupervisorState.Stopped, s.State);
        Assert.IsFalse(File.Exists(t.Paths.PidFile));
        Assert.IsNull(s.Api);
    }

    [TestMethod]
    public async Task Crash_RestartsAutomatically()
    {
        using var t = new TempDir();
        var l = new FakeLauncher();
        using Supervisor s = Make(t, l);
        await s.StartAsync(new TorlinkOptions());
        l.Started[0].Crash();
        await WaitFor(() => l.Started.Count == 2 && s.State == SupervisorState.Running);
        Assert.AreEqual(2, l.Started.Count);
    }

    [TestMethod]
    public async Task CrashLoop_StopsAfterThreeRestartsAndRetryResetsCounter()
    {
        using var t = new TempDir();
        var l = new FakeLauncher();
        var clock = new FakeClock();
        using Supervisor s = Make(t, l, clock);
        await s.StartAsync(new TorlinkOptions());
        for (int i = 0; i < 3; i++)
        {
            FakeProcess last = l.Started[^1];
            int before = l.Started.Count;
            last.Crash();
            await WaitFor(() => l.Started.Count == before + 1 && s.State == SupervisorState.Running);
        }

        l.Started[^1].Crash();
        await WaitFor(() => s.State == SupervisorState.Failed);
        Assert.AreEqual("crash-loop", s.LastError);
        Assert.AreEqual(4, l.Started.Count, "početni + 3 ponovna pokretanja");

        Assert.IsTrue(await s.RetryAsync(new TorlinkOptions()));
        Assert.AreEqual(SupervisorState.Running, s.State);
    }

    [TestMethod]
    public async Task HealthTimeout_FailsAndKillsProcess()
    {
        using var t = new TempDir();
        var l = new FakeLauncher();
        TestRuntime.Install(t.Paths);
        using var s = new Supervisor(t.Paths, l) { HealthProbe = (_, _) => Task.FromResult(false), HealthTimeout = TimeSpan.FromMilliseconds(600) };
        Assert.IsFalse(await s.StartAsync(new TorlinkOptions()));
        Assert.AreEqual(SupervisorState.Failed, s.State);
        Assert.AreEqual("health-timeout", s.LastError);
        Assert.IsTrue(l.Started[0].Killed);
    }

    [TestMethod]
    public async Task StateChanged_IsRaised()
    {
        using var t = new TempDir();
        var l = new FakeLauncher();
        using Supervisor s = Make(t, l);
        var seen = new List<SupervisorState>();
        s.StateChanged += () => seen.Add(s.State);
        await s.StartAsync(new TorlinkOptions());
        await s.StopAsync();
        CollectionAssert.AreEqual(new[] { SupervisorState.Starting, SupervisorState.Running, SupervisorState.Stopped }, seen);
    }

    [TestMethod]
    public async Task InvalidNodeExecutable_IsRejectedAtOnceWithRealLauncher()
    {
        using var t = new TempDir();
        TestRuntime.Install(t.Paths);
        File.WriteAllText(t.Paths.NodeExe, "this is not an executable");
        using var s = new Supervisor(t.Paths, new ProcessLauncher()) { HealthProbe = (_, _) => Task.FromResult(true) };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.IsFalse(await s.StartAsync(new TorlinkOptions()));
        Assert.IsTrue(watch.Elapsed.TotalSeconds < 5, "pokvaren node.exe se odbija odmah");
        Assert.AreEqual(SupervisorState.Failed, s.State);
        Assert.AreEqual("start-failed", s.LastError);
        Assert.ThrowsException<System.ComponentModel.Win32Exception>(() => ProcessLauncher.EnsureExecutable(t.Paths.NodeExe));
    }

    [TestMethod]
    public async Task DeliberateStopOfAHealthyProcess_RemovesTorlinksBootMarker_ButACrashLeavesIt()
    {
        using var t = new TempDir();
        var l = new FakeLauncher();
        using Supervisor s = Make(t, l);
        await s.StartAsync(new TorlinkOptions());
        Directory.CreateDirectory(t.Paths.StateDataDir);
        File.WriteAllText(t.Paths.BootMarkerFile, "{}");
        await s.StopAsync();
        Assert.IsFalse(File.Exists(t.Paths.BootMarkerFile), "namjerno gašenje ne smije natjerati Torlink da sljedeći put vrati sve pauzirano");

        await s.StartAsync(new TorlinkOptions());
        File.WriteAllText(t.Paths.BootMarkerFile, "{}");
        l.Started[^1].Crash();
        await WaitFor(() => l.Started.Count == 3 && s.State == SupervisorState.Running);
        Assert.IsTrue(File.Exists(t.Paths.BootMarkerFile), "pad koji nismo izazvali mi ostaje na Torlinkovoj sigurnosnoj provjeri");
    }

    [TestMethod]
    public async Task MzHeaderButGarbageExecutable_FailsWithAnExceptionNotADialog()
    {
        using var t = new TempDir();
        TestRuntime.Install(t.Paths);
        File.WriteAllBytes(t.Paths.NodeExe, [(byte)'M', (byte)'Z', 1, 2, 3, 4, 5, 6, 7, 8]);
        using var s = new Supervisor(t.Paths, new ProcessLauncher()) { HealthProbe = (_, _) => Task.FromResult(true) };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.IsFalse(await s.StartAsync(new TorlinkOptions()));
        Assert.IsTrue(watch.Elapsed.TotalSeconds < 10, "bez dijaloga koji bi čekao klik");
        Assert.AreEqual("start-failed", s.LastError);
    }

    [TestMethod]
    public void RestartPolicy_AllowsThreePerWindowAndForgetsOldOnes()
    {
        var p = new RestartPolicy();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.IsTrue(p.TryRegister(t0));
        Assert.IsTrue(p.TryRegister(t0.AddSeconds(10)));
        Assert.IsTrue(p.TryRegister(t0.AddSeconds(20)));
        Assert.IsFalse(p.TryRegister(t0.AddSeconds(30)));
        Assert.IsTrue(p.TryRegister(t0.AddMinutes(6)), "stari padovi izlaze iz prozora");
    }

    [TestMethod]
    public void FindFreePort_ReturnsUsablePort()
    {
        int p = Supervisor.FindFreePort();
        Assert.IsTrue(p is > 1023 and < 65536);
    }

    private static async Task WaitFor(Func<bool> cond, int ms = 5000)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < end)
        {
            await Task.Delay(20);
        }
    }
}
