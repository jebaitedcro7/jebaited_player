using System.IO.Compression;

namespace JebaitedPlayer.Diagnostics.Tests;

[TestClass]
public class LogJanitorTests
{
    private string _root = string.Empty;
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0);

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "jp-jan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private string MakeDay(int daysAgo, bool withError = false, int sessionKb = 4)
    {
        DateTime d = Now.Date.AddDays(-daysAgo);
        string dir = Path.Combine(_root, d.ToString("yyyy"), d.ToString("MM"), d.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "session.log"), new string('s', sessionKb * 1024));
        File.WriteAllText(Path.Combine(dir, "errors.log"),
            withError ? "{\"lvl\":\"ERROR\",\"msg\":\"x\"}\n" : "{\"lvl\":\"WARNING\",\"msg\":\"x\"}\n");
        return dir;
    }

    private string ZipPath(int daysAgo, bool err)
    {
        DateTime d = Now.Date.AddDays(-daysAgo);
        return Path.Combine(_root, d.ToString("yyyy"), d.ToString("MM"), d.ToString("yyyy-MM-dd") + (err ? ".err.zip" : ".zip"));
    }

    private static LoggerOptions Opt(Action<LoggerOptions>? t = null)
    {
        var o = new LoggerOptions();
        t?.Invoke(o);
        return o;
    }

    [TestMethod]
    public void PastDays_AreZipped_TodayIsNot()
    {
        string today = MakeDay(0);
        MakeDay(1);
        MakeDay(2, withError: true);

        CleanupResult r = new LogJanitor(_root, Opt()).Run(Now);

        Assert.AreEqual(2, r.Zipped);
        Assert.IsTrue(Directory.Exists(today));
        Assert.IsTrue(File.Exists(ZipPath(1, false)));
        Assert.IsTrue(File.Exists(ZipPath(2, true)), "dan s greškom se označava .err.zip");
        using ZipArchive z = ZipFile.OpenRead(ZipPath(1, false));
        Assert.AreEqual(2, z.Entries.Count);
    }

    [TestMethod]
    public void NormalDays_DeletedAfterKeepDays_ErrorDaysKeptLonger()
    {
        MakeDay(15);
        MakeDay(16, withError: true);
        MakeDay(31, withError: true);
        MakeDay(5);

        CleanupResult r = new LogJanitor(_root, Opt()).Run(Now);

        Assert.IsFalse(File.Exists(ZipPath(15, false)), "normalan dan stariji od 14 dana");
        Assert.IsTrue(File.Exists(ZipPath(16, true)), "dan s greškom ostaje do 30 dana");
        Assert.IsFalse(File.Exists(ZipPath(31, true)), "dan s greškom stariji od 30 dana");
        Assert.IsTrue(File.Exists(ZipPath(5, false)));
        Assert.AreEqual(1, r.ShrunkDays);
        using ZipArchive z = ZipFile.OpenRead(ZipPath(16, true));
        Assert.AreEqual(1, z.Entries.Count, "nakon 14 dana ostaje samo errors.log");
        Assert.AreEqual("errors.log", z.Entries[0].Name);
    }

    [TestMethod]
    public void SizeLimit_RemovesOldestNormalFirst_KeepsErrorDays()
    {
        for (int i = 1; i <= 8; i++)
        {
            MakeDay(i, withError: i == 2, sessionKb: 200);
        }

        var janitor = new LogJanitor(_root, Opt(o => o.TargetBytes = 5L * 1024 * 1024));
        // Zip od 200 KB teksta je malen; spusti cilj ispod ukupnog pomoću nekomprimirajućeg sadržaja.
        foreach (string f in Directory.EnumerateFiles(_root, "session.log", SearchOption.AllDirectories))
        {
            byte[] noise = new byte[900 * 1024];
            new Random(1).NextBytes(noise);
            File.WriteAllBytes(f, noise);
        }

        CleanupResult r = janitor.Run(Now);

        Assert.IsTrue(r.TotalBytes <= 5L * 1024 * 1024 + 8 * 1024, $"ukupno {r.TotalBytes}");
        Assert.IsTrue(File.Exists(ZipPath(2, true)), "dan s greškom preživljava");
        Assert.IsFalse(File.Exists(ZipPath(8, false)), "najstariji normalni dan je obrisan");
        Assert.IsTrue(r.DeletedDays > 0);
    }

    [TestMethod]
    public void ActiveSessionDay_IsNeverZippedOrDeleted()
    {
        string old = MakeDay(20);
        File.WriteAllText(Path.Combine(_root, "active-s-x.session"), $"1|1|{Now.Date.AddDays(-20):O}");

        new LogJanitor(_root, Opt()).Run(Now);

        Assert.IsTrue(Directory.Exists(old), "dan aktivne sesije ostaje netaknut");
        Assert.IsTrue(File.Exists(Path.Combine(old, "session.log")));
    }

    [TestMethod]
    public void UnrelatedFiles_AreNeverTouched()
    {
        string other = Path.Combine(_root, "2026", "11", "notes");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "keep.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "logger_config.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "random.zip"), "x");
        MakeDay(40);

        new LogJanitor(_root, Opt()).Run(Now);

        Assert.IsTrue(File.Exists(Path.Combine(other, "keep.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "logger_config.json")));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "random.zip")));
    }

    [TestMethod]
    public void Run_WritesState_AndHandlesMissingRoot()
    {
        MakeDay(1);
        new LogJanitor(_root, Opt()).Run(Now);
        CleanupResult? state = LogJanitor.ReadState(_root);
        Assert.IsNotNull(state);
        Assert.AreEqual(Now, state.Time);

        CleanupResult empty = new LogJanitor(Path.Combine(_root, "nema"), Opt()).Run(Now);
        Assert.AreEqual(0, empty.Zipped);
    }

    [TestMethod]
    public void Logger_RunJanitor_ReportsAndStaysSafe()
    {
        MakeDay(30);
        var l = new DiagLogger(_root, "1", new LoggerOptions { FlushMs = 20 });
        l.Start();
        CleanupResult? r = l.RunJanitor();
        l.Stop();
        Assert.IsNotNull(r);
        Assert.IsTrue(r.DeletedDays >= 1);
    }
}
