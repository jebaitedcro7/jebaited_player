using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JebaitedPlayer.Diagnostics;

public sealed record ExportItem(DateTime Day, string FileName, long Bytes);

public sealed record ExportPlan(DateTime From, DateTime To, bool Anonymize, IReadOnlyList<ExportItem> Items)
{
    public long TotalBytes => Items.Sum(i => i.Bytes);
}

/// <summary>
/// Lokalni izvoz logova u ZIP radi ručne analize. Prije izvoza se vidi što ulazi (<see cref="Plan"/>). Nazivi datoteka mogu se
/// zamijeniti oznakama (file-1.mkv), a tekst se dodatno čisti (<see cref="Redactor"/>). Ništa se ne šalje nikamo.
/// </summary>
public static partial class LogExporter
{
    [GeneratedRegex("\"file\":\"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex FileProp();

    public static ExportPlan Plan(string root, DateTime from, DateTime to, bool anonymize)
    {
        var items = new List<ExportItem>();
        foreach ((DateTime date, string kind, string path) in LogReader.EnumerateSources(root)
                     .Where(s => s.Date >= from.Date && s.Date <= to.Date).OrderBy(s => s.Date))
        {
            try
            {
                if (kind == "dir")
                {
                    items.AddRange(Directory.EnumerateFiles(path, "*.log").Select(f => new ExportItem(date, Path.GetFileName(f), new FileInfo(f).Length)));
                }
                else
                {
                    using ZipArchive z = ZipFile.OpenRead(path);
                    items.AddRange(z.Entries.Where(e => e.Name.EndsWith(".log", StringComparison.Ordinal)).Select(e => new ExportItem(date, e.Name, e.Length)));
                }
            }
            catch (Exception)
            {
                // Nečitljiv dan se preskače.
            }
        }

        return new ExportPlan(from.Date, to.Date, anonymize, items);
    }

    /// <summary>Piše ZIP; vraća broj upisanih datoteka. Izvorni logovi se ne mijenjaju.</summary>
    public static int Export(string root, ExportPlan plan, string targetZip, string appVersion)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var perDay = new List<(DateTime Day, string File, string Text)>();
        foreach (IGrouping<DateTime, (DateTime Date, string Kind, string Path)> day in LogReader.EnumerateSources(root)
                     .Where(s => s.Date >= plan.From && s.Date <= plan.To).GroupBy(s => s.Date))
        {
            var texts = new Dictionary<string, StringBuilder>();
            foreach ((DateTime _, string kind, string path) in day)
            {
                foreach ((string file, string line) in LinesWithFile(kind, path))
                {
                    if (!texts.TryGetValue(file, out StringBuilder? sb))
                    {
                        texts[file] = sb = new StringBuilder();
                    }

                    sb.AppendLine(line);
                    if (plan.Anonymize)
                    {
                        foreach (Match m in FileProp().Matches(line))
                        {
                            string name = Unescape(m.Groups[1].Value);
                            if (name.Length > 0 && !names.ContainsKey(name))
                            {
                                names[name] = $"file-{names.Count + 1}{Path.GetExtension(name)}";
                            }
                        }
                    }
                }
            }

            foreach ((string file, StringBuilder sb) in texts)
            {
                perDay.Add((day.Key, file, sb.ToString()));
            }
        }

        string tmp = targetZip + ".tmp";
        if (File.Exists(tmp))
        {
            File.Delete(tmp);
        }

        int written = 0;
        using (ZipArchive zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach ((DateTime day, string file, string text) in perDay)
            {
                string content = plan.Anonymize ? Anonymize(text, names) : text;
                WriteEntry(zip, $"{day:yyyy-MM-dd}/{file}", content);
                written++;
            }

            WriteEntry(zip, "README.txt",
                $"jebaited_player logs, exported locally {DateTime.Now:yyyy-MM-dd HH:mm}.\n" +
                $"App version: {appVersion}. OS: {Environment.OSVersion.VersionString}.\n" +
                $"Range: {plan.From:yyyy-MM-dd} to {plan.To:yyyy-MM-dd}. Files: {written}.\n" +
                (plan.Anonymize ? "File names were replaced with file-N labels.\n" : "File names are included as logged.\n") +
                "Format: JSON Lines (one event per line). Nothing in this archive was sent anywhere.\n");
        }

        File.Move(tmp, targetZip, true);
        return written;
    }

    private static IEnumerable<(string File, string Line)> LinesWithFile(string kind, string path)
    {
        if (kind == "dir")
        {
            foreach (string f in Directory.EnumerateFiles(path, "*.log"))
            {
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                string? l;
                while ((l = reader.ReadLine()) != null)
                {
                    yield return (Path.GetFileName(f), l);
                }
            }

            yield break;
        }

        using ZipArchive z = ZipFile.OpenRead(path);
        foreach (ZipArchiveEntry e in z.Entries.Where(e => e.Name.EndsWith(".log", StringComparison.Ordinal)))
        {
            using var sr = new StreamReader(e.Open(), Encoding.UTF8);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                yield return (e.Name, line);
            }
        }
    }

    private static string Anonymize(string text, Dictionary<string, string> names)
    {
        foreach ((string original, string label) in names.OrderByDescending(n => n.Key.Length))
        {
            text = text.Replace(original, label, StringComparison.OrdinalIgnoreCase);
            string escaped = JsonSerializer.Serialize(original).Trim('"');
            if (escaped != original)
            {
                text = text.Replace(escaped, label, StringComparison.OrdinalIgnoreCase);
            }
        }

        return Redactor.Clean(text);
    }

    private static string Unescape(string jsonString)
    {
        try
        {
            return JsonSerializer.Deserialize<string>("\"" + jsonString + "\"") ?? jsonString;
        }
        catch (JsonException)
        {
            return jsonString;
        }
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }
}
