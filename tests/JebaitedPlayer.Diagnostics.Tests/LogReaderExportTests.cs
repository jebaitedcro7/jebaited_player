using System.IO.Compression;

namespace JebaitedPlayer.Diagnostics.Tests;

[TestClass]
public class LogReaderExportTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup() => _root = Path.Combine(Path.GetTempPath(), "jp-rd-" + Guid.NewGuid().ToString("N"));

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

    private void WriteSession()
    {
        var l = new DiagLogger(_root, "1.2.0", new LoggerOptions { FlushMs = 20 });
        l.Start();
        l.Log(LogLevel.Info, LogCat.Playback, "file.loaded", "T", "File loaded", ("file", "Tajni Film 2024.mkv"));
        l.Log(LogLevel.Info, LogCat.Playback, "file.loaded", "T", "File loaded", ("file", "Druga Epizoda.mp4"));
        l.Log(LogLevel.Warning, LogCat.Subtitle, "subtitle.load.failed", "T", "Subtitle not loaded", ("file", "Tajni Film 2024.srt"));
        l.Log(LogLevel.Error, LogCat.Video, "mpv.video.decode", "T", "Error while decoding frame of Tajni Film 2024.mkv", ("file", "Tajni Film 2024.mkv"));
        l.Log(LogLevel.Error, LogCat.Video, "mpv.video.decode", "T", "Error while decoding frame", ("file", "Druga Epizoda.mp4"));
        l.Stop();
    }

    [TestMethod]
    public void Summary_CountsSessionsPlaysAndProblems()
    {
        WriteSession();
        LogSummary s = LogReader.Summarize(_root);

        Assert.AreEqual(1, s.Launches);
        Assert.AreEqual(2, s.SuccessfulPlays);
        Assert.AreEqual(2, s.Errors);
        Assert.AreEqual(1, s.Warnings);
        Assert.AreEqual(1, s.Sessions.Count);
        Assert.IsTrue(s.Sessions[0].Clean);
        Assert.AreEqual("mpv.video.decode", s.TopProblems[0].Type);
        Assert.AreEqual(2, s.TopProblems[0].Count);
        Assert.IsTrue(s.RecentProblems.Count >= 3);
        Assert.IsTrue(s.DiskBytes > 0);
    }

    [TestMethod]
    public void Read_FiltersByDateAndDeduplicates()
    {
        WriteSession();
        var all = LogReader.Read(_root, DateTime.Today, DateTime.Today).ToList();
        Assert.AreEqual(all.Count, all.Select(e => (e.SessionId, e.Seq)).Distinct().Count(), "ctx u errors.log ne duplicira session.log");
        Assert.IsTrue(all.Any(e => e.Level == LogLevel.Error && e.Data.Contains("Druga Epizoda.mp4", StringComparison.Ordinal)));
        Assert.AreEqual(0, LogReader.Read(_root, DateTime.Today.AddDays(-30), DateTime.Today.AddDays(-20)).Count());
        Assert.IsTrue(LogReader.Read(_root, DateTime.Today, DateTime.Today, errorsOnly: true).All(e => e.Level >= LogLevel.Info));
    }

    [TestMethod]
    public void Crash_IsVisibleInSummary()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "active-s-dead.session"), $"{int.MaxValue - 9}|1|{DateTimeOffset.Now:O}");
        var l = new DiagLogger(_root, "1", new LoggerOptions { FlushMs = 20 });
        l.Start();
        l.Stop();
        LogSummary s = LogReader.Summarize(_root);
        Assert.AreEqual(1, s.Crashes);
    }

    [TestMethod]
    public void Export_Anonymizes_AndDoesNotChangeSource()
    {
        WriteSession();
        string src = Directory.EnumerateFiles(_root, "session.log", SearchOption.AllDirectories).Single();
        string before = File.ReadAllText(src);

        ExportPlan plan = LogExporter.Plan(_root, DateTime.Today, DateTime.Today, anonymize: true);
        Assert.IsTrue(plan.Items.Any(i => i.FileName == "errors.log"));
        Assert.IsTrue(plan.TotalBytes > 0);

        string target = Path.Combine(_root, "export.zip");
        int n = LogExporter.Export(_root, plan, target, "1.2.0");

        Assert.IsTrue(n >= 2);
        Assert.AreEqual(before, File.ReadAllText(src));
        using ZipArchive z = ZipFile.OpenRead(target);
        Assert.IsNotNull(z.GetEntry("README.txt"));
        foreach (ZipArchiveEntry e in z.Entries)
        {
            using var sr = new StreamReader(e.Open());
            string text = sr.ReadToEnd();
            Assert.IsFalse(text.Contains("Tajni Film", StringComparison.Ordinal), e.FullName);
            Assert.IsFalse(text.Contains("Druga Epizoda", StringComparison.Ordinal), e.FullName);
        }

        using var sess = new StreamReader(z.Entries.First(e => e.Name == "session.log").Open());
        StringAssert.Contains(sess.ReadToEnd(), "file-1.mkv");
    }

    [TestMethod]
    public void Export_WithoutAnonymize_KeepsNames_AndReadsZippedDays()
    {
        WriteSession();
        // Pretvori današnji dan u zip kao što radi janitor za prošle dane.
        string dayDir = Directory.EnumerateDirectories(_root, DateTime.Today.ToString("yyyy-MM-dd"), SearchOption.AllDirectories).Single();
        string zip = dayDir + ".zip";
        ZipFile.CreateFromDirectory(dayDir, zip);
        Directory.Delete(dayDir, true);

        Assert.IsTrue(LogReader.Read(_root, DateTime.Today, DateTime.Today).Any(e => e.Type == "file.loaded"));
        ExportPlan plan = LogExporter.Plan(_root, DateTime.Today, DateTime.Today, anonymize: false);
        string target = Path.Combine(_root, "plain.zip");
        LogExporter.Export(_root, plan, target, "1");
        using ZipArchive z = ZipFile.OpenRead(target);
        using var sr = new StreamReader(z.Entries.First(e => e.Name == "session.log").Open());
        StringAssert.Contains(sr.ReadToEnd(), "Tajni Film 2024.mkv");
    }
}
