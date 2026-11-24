using System.Diagnostics;
using System.Text.Json;

namespace JebaitedPlayer.Diagnostics.Tests;

[TestClass]
public class DiagLoggerTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup() => _dir = Path.Combine(Path.GetTempPath(), "jp-diag-" + Guid.NewGuid().ToString("N"));

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

    private static LoggerOptions Fast(Action<LoggerOptions>? tweak = null)
    {
        var o = new LoggerOptions { FlushMs = 20 };
        tweak?.Invoke(o);
        return o;
    }

    private static string Day(DiagLogger l, string file) => Path.Combine(l.DayDir(DateTime.Now), file);

    private static string[] Lines(string path) => File.Exists(path) ? File.ReadAllLines(path) : [];

    [TestMethod]
    public void StartStop_WritesSessionAndRemovesMarker()
    {
        var l = new DiagLogger(_dir, "9.9.9", Fast());
        l.Start();
        Assert.IsTrue(File.Exists(l.MarkerFile));
        l.Log(LogLevel.Info, LogCat.Usage, "app.open", "Test", "Otvoreno", ("n", 3));
        l.Stop();

        Assert.IsFalse(File.Exists(l.MarkerFile), "čist izlaz briše marker");
        string[] lines = Lines(Day(l, "session.log"));
        Assert.IsTrue(lines.Length >= 3);
        using var first = JsonDocument.Parse(lines[0]);
        Assert.AreEqual("session.start", first.RootElement.GetProperty("type").GetString());
        Assert.AreEqual(l.SessionId, first.RootElement.GetProperty("sid").GetString());
        Assert.AreEqual("9.9.9", first.RootElement.GetProperty("ver").GetString());
        Assert.IsTrue(lines.Any(x => x.Contains("\"type\":\"session.end\"") && x.Contains("\"clean\":true")));
        Assert.IsTrue(lines.Any(x => x.Contains("\"n\":3")));
    }

    [TestMethod]
    public void Error_WritesContextBeforeAndAfter()
    {
        var l = new DiagLogger(_dir, "1", Fast(o => o.ContextAfter = 2));
        l.Start();
        l.Log(LogLevel.Debug, LogCat.Playback, "ctx.before", "T", "prije-debug");
        l.Log(LogLevel.Info, LogCat.Playback, "ctx.before", "T", "prije-info");
        l.Log(LogLevel.Error, LogCat.Video, "decode.video", "T", "Greška dekodiranja", new InvalidOperationException("boom"));
        l.Log(LogLevel.Info, LogCat.Playback, "ctx.after", "T", "poslije-1");
        l.Log(LogLevel.Info, LogCat.Playback, "ctx.after", "T", "poslije-2");
        l.Log(LogLevel.Info, LogCat.Playback, "ctx.after", "T", "poslije-3");
        l.Stop();

        string errors = File.ReadAllText(Day(l, "errors.log"));
        StringAssert.Contains(errors, "prije-debug");
        StringAssert.Contains(errors, "prije-info");
        StringAssert.Contains(errors, "Greška dekodiranja");
        StringAssert.Contains(errors, "boom");
        StringAssert.Contains(errors, "poslije-1");
        StringAssert.Contains(errors, "poslije-2");
        Assert.IsFalse(errors.Contains("poslije-3"), "kontekst poslije je ograničen");

        string session = File.ReadAllText(Day(l, "session.log"));
        Assert.IsFalse(session.Contains("prije-debug"), "DEBUG nije u session.log");
        Assert.IsFalse(File.Exists(Day(l, "diagnostics.log")));
    }

    [TestMethod]
    public void AbandonedMarker_ReportsCrash_LiveMarkerIgnored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "active-s-old.session"), $"{int.MaxValue - 7}|1|2026-11-18");
        using Process me = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(_dir, "active-s-live.session"), $"{me.Id}|{me.StartTime.ToUniversalTime().Ticks}|x");

        var l = new DiagLogger(_dir, "1", Fast());
        l.Start();
        l.Stop();

        string errors = File.ReadAllText(Day(l, "errors.log"));
        StringAssert.Contains(errors, "session.crashed");
        StringAssert.Contains(errors, "s-old");
        Assert.IsFalse(errors.Contains("s-live"));
        Assert.IsFalse(File.Exists(Path.Combine(_dir, "active-s-old.session")));
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "active-s-live.session")));
    }

    [TestMethod]
    public void Abort_KeepsMarker_NextStartReportsCrash()
    {
        var a = new DiagLogger(_dir, "1", Fast());
        a.Start();
        a.Log(LogLevel.Critical, LogCat.Stability, "crash.unhandled", "T", "pad");
        a.Abort();
        Assert.IsTrue(File.Exists(a.MarkerFile));
        StringAssert.Contains(File.ReadAllText(Day(a, "errors.log")), "crash.unhandled");

        // Isti proces je još živ, pa ga marker ne prijavljuje; simuliramo smrt procesa.
        File.WriteAllText(a.MarkerFile, $"{int.MaxValue - 7}|1|x");
        var b = new DiagLogger(_dir, "1", Fast());
        b.Start();
        b.Stop();
        StringAssert.Contains(File.ReadAllText(Day(b, "errors.log")), a.SessionId);
    }

    [TestMethod]
    public void FullQueue_DropsInfoButKeepsErrors_AndCountsDropped()
    {
        var l = new DiagLogger(_dir, "1", Fast(o =>
        {
            o.QueueCapacity = 64;
            o.FlushMs = 400;
        }));
        l.Start();
        for (int i = 0; i < 2000; i++)
        {
            l.Log(LogLevel.Info, LogCat.Usage, "flood", "T", "x" + i);
        }

        l.Log(LogLevel.Error, LogCat.Stability, "must.survive", "T", "važna greška");
        l.Stop(TimeSpan.FromSeconds(5));

        Assert.IsTrue(l.DroppedCount > 0);
        string errors = File.ReadAllText(Day(l, "errors.log"));
        StringAssert.Contains(errors, "must.survive");
        StringAssert.Contains(errors, "logger.dropped");
        StringAssert.Contains(File.ReadAllText(l.StatusFile), "dropped");
    }

    [TestMethod]
    public void Rotation_KeepsConfiguredNumberOfFiles()
    {
        var l = new DiagLogger(_dir, "1", Fast(o =>
        {
            o.MaxFileBytes = 256 * 1024;
            o.MaxRotations = 2;
        }));
        l.Start();
        string pad = new('a', 900);
        for (int i = 0; i < 1500; i++)
        {
            l.Log(LogLevel.Info, LogCat.Usage, "fill", "T", pad);
            if (i % 200 == 0)
            {
                Thread.Sleep(60);
            }
        }

        l.Stop(TimeSpan.FromSeconds(10));
        string dir = l.DayDir(DateTime.Now);
        Assert.IsTrue(File.Exists(Path.Combine(dir, "session.1.log")));
        Assert.IsFalse(File.Exists(Path.Combine(dir, "session.3.log")));
        Assert.IsTrue(new FileInfo(Path.Combine(dir, "session.log")).Length <= 256 * 1024 + 2000);
    }

    [TestMethod]
    public void WriteFailures_NeverThrow_AndAreReportedInStatus()
    {
        Directory.CreateDirectory(_dir);
        var l = new DiagLogger(_dir, "1", Fast());
        // Na mjesto godišnje mape stavimo datoteku: svako stvaranje dnevne mape pada.
        File.WriteAllText(Path.Combine(_dir, DateTime.Now.ToString("yyyy")), "blokada");
        l.Start();
        for (int i = 0; i < 50; i++)
        {
            l.Log(LogLevel.Error, LogCat.App, "e", "T", "m" + i);
        }

        l.Stop(TimeSpan.FromSeconds(5));
        StringAssert.Contains(File.ReadAllText(l.StatusFile), "failed");
    }

    [TestMethod]
    public void NotStarted_LoggingIsNoOp()
    {
        var l = new DiagLogger(_dir, "1", Fast());
        l.Log(LogLevel.Error, LogCat.App, "x", "T", "ništa");
        l.Stop();
        Assert.IsFalse(Directory.Exists(_dir));
    }

    [TestMethod]
    public void Disabled_WritesNothing()
    {
        var l = new DiagLogger(_dir, "1", Fast(o => o.Enabled = false));
        l.Start();
        l.Log(LogLevel.Error, LogCat.App, "x", "T", "ništa");
        l.Stop();
        Assert.IsFalse(Directory.Exists(_dir));
    }

    [TestMethod]
    public void Croatian_And_Secrets_AreHandled()
    {
        var l = new DiagLogger(_dir, "1", Fast());
        l.Start();
        l.Log(LogLevel.Info, LogCat.Subtitle, "subtitle.encoding", "T", "Titl čćšđž, apikey=ABC123 u C:\\Users\\Pero\\Videos\\f.mkv",
            ("file", "C:\\Users\\Pero\\x.srt"));
        l.Stop();
        string s = File.ReadAllText(Day(l, "session.log"));
        StringAssert.Contains(s, "čćšđž");
        Assert.IsFalse(s.Contains("ABC123"));
        Assert.IsFalse(s.Contains("Pero"));
    }
}

[TestClass]
public class RedactorTests
{
    [TestMethod]
    public void Hides_iptv_credentials_in_paths_and_queries()
    {
        Assert.AreEqual("Opening http://srv.example:80/live/***/***/123.m3u8", Redactor.Clean("Opening http://srv.example:80/live/mojeime/mojalozinka/123.m3u8"));
        Assert.AreEqual("http://s/player_api.php?username=***&password=***", Redactor.Clean("http://s/player_api.php?username=ime&password=lozinka"));
    }

    [TestMethod]
    public void Clean_RemovesProfileAndSecrets()
    {
        Assert.AreEqual(@"<dir>\a.mkv", Redactor.Clean(@"C:\Users\Some User\Videos\a.mkv"));
        Assert.AreEqual(@"Titl <dir>\x.srt (SRT, UTF-8)", Redactor.Clean(@"Titl D:\Moj film\Sezona 1\x.srt (SRT, UTF-8)"));
        Assert.AreEqual(@"<dir>\x.srt", Redactor.Clean(@"\\server\share\mapa\x.srt"));
        Assert.AreEqual("password=***", Redactor.Clean("password=hunter2"));
        Assert.AreEqual("https://x/api?apikey=***&q=1", Redactor.Clean("https://x/api?apikey=SECRET&q=1"));
    }

    [TestMethod]
    public void FileName_StripsFolder()
    {
        Assert.AreEqual("film.mkv", Redactor.FileName(@"D:\Privatno\film.mkv"));
        Assert.AreEqual(string.Empty, Redactor.FileName(null));
    }
}

[TestClass]
public class NoNetworkTests
{
    [TestMethod]
    public void DiagnosticsProject_HasNoNetworkCode()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src", "JebaitedPlayer.Diagnostics")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.IsNotNull(dir, "repozitorij nije pronađen");
        string project = Path.Combine(dir, "src", "JebaitedPlayer.Diagnostics");
        string[] banned = ["System.Net.", "HttpClient", "Socket", "WebRequest", "TcpClient", "Smtp", "Process.Start"];
        string sep = Path.DirectorySeparatorChar.ToString();
        foreach (string file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains(sep + "obj" + sep) && !f.Contains(sep + "bin" + sep)))
        {
            string text = File.ReadAllText(file);
            foreach (string b in banned)
            {
                Assert.IsFalse(text.Contains(b), $"{Path.GetFileName(file)} sadrži '{b}'");
            }
        }

        Assert.IsFalse(File.ReadAllText(Path.Combine(project, "JebaitedPlayer.Diagnostics.csproj")).Contains("PackageReference"));
    }
}
