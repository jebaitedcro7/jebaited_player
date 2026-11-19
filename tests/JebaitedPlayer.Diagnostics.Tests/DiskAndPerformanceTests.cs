using System.Diagnostics;

namespace JebaitedPlayer.Diagnostics.Tests;

[TestClass]
public class DiskAndPerformanceTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup() => _dir = Path.Combine(Path.GetTempPath(), "jp-dp-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private static string Day(DiagLogger l, string f) => Path.Combine(l.DayDir(DateTime.Now), f);

    [TestMethod]
    public void LowDisk_WritesOnlyWarningsAndAbove()
    {
        // Prag "malo mjesta" viši od stvarnog slobodnog prostora simulira gotovo pun disk bez diranja diska.
        var l = new DiagLogger(_dir, "1", new LoggerOptions { FlushMs = 20, LowDiskBytes = long.MaxValue / 2 });
        l.Start();
        Assert.IsNotNull(l.RunJanitor());
        l.Log(LogLevel.Info, LogCat.Usage, "x.info", "T", "obična poruka");
        l.Log(LogLevel.Warning, LogCat.Usage, "x.warn", "T", "upozorenje");
        l.Log(LogLevel.Error, LogCat.Usage, "x.err", "T", "greška");
        l.Stop();

        string session = File.ReadAllText(Day(l, "session.log"));
        Assert.IsFalse(session.Contains("obična poruka"));
        StringAssert.Contains(session, "upozorenje");
        StringAssert.Contains(session, "greška");
        StringAssert.Contains(File.ReadAllText(l.StatusFile), "Low disk space");
    }

    [TestMethod]
    public void CriticalDisk_PausesWritingWithoutThrowing()
    {
        var l = new DiagLogger(_dir, "1", new LoggerOptions { FlushMs = 20, CriticalDiskBytes = long.MaxValue / 2, LowDiskBytes = long.MaxValue / 2 });
        l.Start();
        l.RunJanitor();
        for (int i = 0; i < 100; i++)
        {
            l.Log(LogLevel.Critical, LogCat.Stability, "x.crit", "T", "kritično " + i);
        }

        l.Stop();
        Assert.IsFalse(File.Exists(Day(l, "errors.log")) && File.ReadAllText(Day(l, "errors.log")).Contains("kritično 99"));
        StringAssert.Contains(File.ReadAllText(l.StatusFile), "logging paused");
    }

    [TestMethod]
    public void Logging_IsCheapForTheCaller_AndWriterStaysSmall()
    {
        var l = new DiagLogger(_dir, "1", new LoggerOptions { FlushMs = 250 });
        l.Start();
        using Process proc = Process.GetCurrentProcess();
        proc.Refresh();
        TimeSpan cpuBefore = proc.TotalProcessorTime;
        long memBefore = GC.GetAllocatedBytesForCurrentThread();

        const int n = 20_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            l.Log(LogLevel.Info, LogCat.Usage, "perf.event", "T", "Setting changed", ("name", "volume"), ("value", i));
        }

        sw.Stop();
        double perEventUs = sw.Elapsed.TotalMilliseconds * 1000 / n;
        long allocPerEvent = (GC.GetAllocatedBytesForCurrentThread() - memBefore) / n;

        l.Stop(TimeSpan.FromSeconds(15));
        proc.Refresh();
        double cpuMs = (proc.TotalProcessorTime - cpuBefore).TotalMilliseconds;

        TestContext?.WriteLine($"caller: {perEventUs:0.00} us/event, {allocPerEvent} B/event; total CPU for {n} events incl. writer: {cpuMs:0} ms; dropped {l.DroppedCount}");
        Assert.IsTrue(perEventUs < 50, $"poziv iz aplikacije mora biti brz, izmjereno {perEventUs:0.0} us");
        Assert.IsTrue(allocPerEvent < 1024, $"alokacija po događaju {allocPerEvent} B");
    }

    public TestContext? TestContext { get; set; }
}
