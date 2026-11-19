using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace JebaitedPlayer.Diagnostics;

public sealed record LogEntry(
    DateTimeOffset Time, LogLevel Level, string Category, string Type, string Component, string Message,
    string SessionId, long Seq, string Data, string? Exception)
{
    public string Day => Time.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public string LevelText => Level.ToString().ToUpperInvariant();

    public string TimeText => Time.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
}

public sealed record SessionInfo(string Id, DateTimeOffset Start, DateTimeOffset End, bool Clean, bool Crashed, int Warnings, int Errors, int Plays)
{
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
}

public sealed record ProblemGroup(string Type, string Category, LogLevel Level, int Count, DateTimeOffset Last);

public sealed class LogSummary
{
    public TimeSpan TotalUsage { get; set; }
    public int Launches { get; set; }
    public int SuccessfulPlays { get; set; }
    public int Errors { get; set; }
    public int Warnings { get; set; }
    public int Crashes { get; set; }
    public long DiskBytes { get; set; }
    public DateTime? LastCleanup { get; set; }
    public int Days { get; set; }
    public List<LogEntry> RecentProblems { get; } = [];
    public List<ProblemGroup> TopProblems { get; } = [];
    public List<SessionInfo> Sessions { get; } = [];
}

/// <summary>Čitanje dnevnika s diska (mape dana i zip arhive) za prozor Logs. Nikad ne mijenja datoteke.</summary>
public static class LogReader
{
    private const int MaxEntries = 50_000;

    /// <summary>Dani za koje postoji zapis, najnoviji prvi.</summary>
    public static List<DateTime> AvailableDays(string root)
    {
        var days = new HashSet<DateTime>();
        foreach ((DateTime date, _, _) in EnumerateSources(root))
        {
            days.Add(date);
        }

        return [.. days.OrderByDescending(d => d)];
    }

    public static IEnumerable<LogEntry> Read(string root, DateTime from, DateTime to, bool errorsOnly = false)
    {
        var seen = new HashSet<(string, long)>();
        int count = 0;
        foreach ((DateTime date, string kind, string path) in EnumerateSources(root).Where(s => s.Date >= from.Date && s.Date <= to.Date)
                     .OrderByDescending(s => s.Date))
        {
            foreach (string line in Lines(kind, path, errorsOnly))
            {
                LogEntry? e = Parse(line);
                if (e is null || !seen.Add((e.SessionId, e.Seq)))
                {
                    continue;
                }

                if (++count > MaxEntries)
                {
                    yield break;
                }

                yield return e;
            }
        }
    }

    public static LogSummary Summarize(string root, int days = 30)
    {
        var s = new LogSummary();
        DateTime to = DateTime.Now.Date;
        DateTime from = to.AddDays(-days + 1);
        var groups = new Dictionary<string, (string Cat, LogLevel Lvl, int Count, DateTimeOffset Last)>();
        var sessions = new Dictionary<string, (DateTimeOffset Start, DateTimeOffset End, bool Clean, int W, int E, int P)>();
        var crashedIds = new HashSet<string>();
        var recent = new List<LogEntry>();

        foreach (LogEntry e in Read(root, from, to))
        {
            (DateTimeOffset Start, DateTimeOffset End, bool Clean, int W, int E, int P) cur = sessions.TryGetValue(e.SessionId, out var v)
                ? v : (e.Time, e.Time, false, 0, 0, 0);
            if (e.Time < cur.Start)
            {
                cur.Start = e.Time;
            }

            if (e.Time > cur.End)
            {
                cur.End = e.Time;
            }

            if (e.Type == "session.start")
            {
                s.Launches++;
            }
            else if (e.Type == "session.end")
            {
                cur.Clean = true;
            }
            else if (e.Type == "file.loaded")
            {
                s.SuccessfulPlays++;
                cur.P++;
            }
            else if (e.Type == "session.crashed")
            {
                s.Crashes++;
                if (e.Data.Split(',').Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith("previousSession=", StringComparison.Ordinal)) is { } prev)
                {
                    crashedIds.Add(prev["previousSession=".Length..]);
                }
            }

            if (e.Level == LogLevel.Warning)
            {
                s.Warnings++;
                cur.W++;
            }
            else if (e.Level >= LogLevel.Error)
            {
                s.Errors++;
                cur.E++;
            }

            if (e.Level >= LogLevel.Warning && e.Category != LogCat.Logger)
            {
                groups[e.Type] = groups.TryGetValue(e.Type, out var g)
                    ? (g.Cat, (LogLevel)Math.Max((int)g.Lvl, (int)e.Level), g.Count + 1, e.Time > g.Last ? e.Time : g.Last)
                    : (e.Category, e.Level, 1, e.Time);
                recent.Add(e);
            }

            sessions[e.SessionId] = cur;
        }

        foreach ((string id, var v) in sessions)
        {
            var info = new SessionInfo(id, v.Start, v.End, v.Clean, crashedIds.Contains(id), v.W, v.E, v.P);
            s.Sessions.Add(info);
            s.TotalUsage += info.Duration;
        }

        s.Sessions.Sort((a, b) => b.Start.CompareTo(a.Start));
        s.TopProblems.AddRange(groups.Select(kv => new ProblemGroup(kv.Key, kv.Value.Cat, kv.Value.Lvl, kv.Value.Count, kv.Value.Last))
            .OrderByDescending(g => g.Count).ThenByDescending(g => g.Last).Take(15));
        s.RecentProblems.AddRange(recent.OrderByDescending(r => r.Time).ThenByDescending(r => r.Seq).Take(20));
        s.Days = AvailableDays(root).Count;
        s.DiskBytes = DirSize(root);
        s.LastCleanup = LogJanitor.ReadState(root)?.Time;
        return s;
    }

    private static long DirSize(string root)
    {
        try
        {
            return Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Izvori po danu: ("dir"|"zip", putanja).</summary>
    internal static IEnumerable<(DateTime Date, string Kind, string Path)> EnumerateSources(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (string year in SafeDirs(root))
        {
            foreach (string month in SafeDirs(year))
            {
                foreach (string dir in SafeDirs(month))
                {
                    if (TryDate(Path.GetFileName(dir), out DateTime d))
                    {
                        yield return (d, "dir", dir);
                    }
                }

                foreach (string zip in Directory.EnumerateFiles(month, "*.zip"))
                {
                    string name = Path.GetFileName(zip);
                    if (name.Length >= 10 && TryDate(name[..10], out DateTime d))
                    {
                        yield return (d, "zip", zip);
                    }
                }
            }
        }
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static bool TryDate(string name, out DateTime d) =>
        DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

    internal static IEnumerable<string> Lines(string kind, string path, bool errorsOnly)
    {
        var lines = new List<string>();
        try
        {
            if (kind == "dir")
            {
                foreach (string f in Directory.EnumerateFiles(path, "*.log").OrderBy(f => f, StringComparer.Ordinal))
                {
                    string n = Path.GetFileName(f);
                    if (errorsOnly && !n.StartsWith("errors", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(fs, Encoding.UTF8);
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        lines.Add(line);
                    }
                }
            }
            else
            {
                using ZipArchive zip = ZipFile.OpenRead(path);
                foreach (ZipArchiveEntry e in zip.Entries.Where(e => e.Name.EndsWith(".log", StringComparison.Ordinal)))
                {
                    if (errorsOnly && !e.Name.StartsWith("errors", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    using var sr = new StreamReader(e.Open(), Encoding.UTF8);
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        lines.Add(line);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Nečitljiv dan se preskače.
        }

        return lines;
    }

    internal static LogEntry? Parse(string line)
    {
        if (line.Length < 10 || line[0] != '{')
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            JsonElement r = doc.RootElement;
            string Str(string n) => r.TryGetProperty(n, out JsonElement x) ? x.ToString() : string.Empty;
            if (!DateTimeOffset.TryParse(Str("t"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset t))
            {
                return null;
            }

            LogLevel lvl = Enum.TryParse(Str("lvl"), true, out LogLevel l) ? l : LogLevel.Info;
            string data = string.Empty;
            if (r.TryGetProperty("d", out JsonElement d) && d.ValueKind == JsonValueKind.Object)
            {
                data = string.Join(", ", d.EnumerateObject().Select(p => $"{p.Name}={p.Value}"));
            }

            long.TryParse(Str("seq"), CultureInfo.InvariantCulture, out long seq);
            return new LogEntry(t, lvl, Str("cat"), Str("type"), Str("comp"), Str("msg"), Str("sid"), seq, data,
                r.TryGetProperty("ex", out JsonElement ex) ? ex.GetString() : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
