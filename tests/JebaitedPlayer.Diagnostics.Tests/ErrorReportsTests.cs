using JebaitedPlayer.Diagnostics;

namespace JebaitedPlayer.Diagnostics.Tests;

/// <summary>Ciklus 6, N1: redigirani izvještaji, lokalni red i slanje.</summary>
[TestClass]
public sealed class ErrorReportsTests
{
    private static readonly DateTime T0 = new(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);
    private string _dir = string.Empty;

    [TestInitialize]
    public void Init() => _dir = Path.Combine(Path.GetTempPath(), "jp-reports-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static ErrorReport Report(string message, Exception? ex = null, DateTime? at = null) =>
        ErrorReport.Create(LogLevel.Error, "playback", "file.error", "Test", message, ex, "1.8.4", "GitHub", at ?? T0);

    [TestMethod]
    public void Report_has_no_folders_names_or_keys()
    {
        Exception ex;
        try
        {
            throw new IOException(@"Ne mogu otvoriti C:\Users\Ivan Horvat\Videos\Obiteljski.Film.mkv");
        }
        catch (IOException e)
        {
            ex = e;
        }

        ErrorReport r = Report(@"Greška pri D:\Filmovi\Tajno\Film.mkv, api_key=ABC123SECRET", ex);
        string all = r.Message + r.Exception;
        Assert.IsFalse(all.Contains("Ivan Horvat", StringComparison.Ordinal));
        Assert.IsFalse(all.Contains(@"\Tajno\", StringComparison.Ordinal));
        Assert.IsFalse(all.Contains("ABC123SECRET", StringComparison.Ordinal));
        StringAssert.Contains(r.Message, "Film.mkv");
        Assert.AreEqual("1.8.4", r.AppVersion);
    }

    [TestMethod]
    public void Queue_skips_duplicates_and_limits_per_session_count_size_and_age()
    {
        var q = new ErrorReportQueue(_dir);
        Assert.IsTrue(q.Add(Report("prva"), T0));
        Assert.IsFalse(q.Add(Report("prva"), T0), "Ista greška samo jednom po sesiji.");
        for (int i = 0; i < 10; i++)
        {
            q.Add(Report("greška " + i), T0);
        }

        Assert.AreEqual(ErrorReportQueue.MaxPerSession, q.Pending().Count, "Najviše 5 po sesiji.");

        // Nova sesija (novi red): stari izvještaji se brišu nakon 14 dana, a broj je ograničen na 20.
        var next = new ErrorReportQueue(_dir);
        foreach (FileInfo f in new DirectoryInfo(_dir).GetFiles())
        {
            f.LastWriteTimeUtc = T0.AddDays(-20);
        }

        next.Prune(T0);
        Assert.AreEqual(0, next.Pending().Count);
        for (int s = 0; s < 6; s++)
        {
            var session = new ErrorReportQueue(_dir);
            for (int i = 0; i < 5; i++)
            {
                session.Add(Report($"s{s} g{i}", at: T0.AddSeconds(s * 10 + i)), T0);
            }
        }

        Assert.AreEqual(ErrorReportQueue.MaxReports, new ErrorReportQueue(_dir).Pending().Count);
    }

    [TestMethod]
    public void GitHub_issue_link_contains_the_redacted_reports_and_stays_short()
    {
        var q = new ErrorReportQueue(_dir);
        q.Add(Report(@"Ne radi C:\Users\Ana\x.mkv"), T0);
        string url = q.GitHubIssueUrl("https://github.com/jebaitedcro7/jebaited_player", "1.8.4");
        StringAssert.StartsWith(url, "https://github.com/jebaitedcro7/jebaited_player/issues/new?title=Diagnostic%20report%201.8.4&body=");
        string body = Uri.UnescapeDataString(url[(url.IndexOf("&body=", StringComparison.Ordinal) + 6)..]);
        StringAssert.Contains(body, "x.mkv");
        Assert.IsFalse(body.Contains("Ana", StringComparison.Ordinal));
        Assert.IsTrue(url.Length < 20000);
    }
}
