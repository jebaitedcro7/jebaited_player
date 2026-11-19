using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JebaitedPlayer.Diagnostics;

/// <summary>Što je čišćenje napravilo (zapisuje se i u logger_state.json za prikaz u prozoru Logs).</summary>
public sealed class CleanupResult
{
    public DateTime Time { get; set; }
    public int Zipped { get; set; }
    public int DeletedDays { get; set; }
    public int ShrunkDays { get; set; }
    public long FreedBytes { get; set; }
    public long TotalBytes { get; set; }
    public long FreeDiskBytes { get; set; } = -1;
}

/// <summary>
/// Održavanje logova: sažimanje prošlih dana u zip, brisanje starih, ograničenje ukupne veličine, provjera diska.
/// Radi isključivo unutar mape logova i samo nad datotekama čija imena odgovaraju obrascima dnevnika (GGGG-MM-DD i GGGG-MM-DD.zip).
/// Nikad ne dira današnji dan, dane s aktivnom sesijom ni markere sesija.
/// </summary>
public sealed partial class LogJanitor(string root, LoggerOptions options)
{
    public const string StateFile = "logger_state.json";

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DayName();

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})(\.err)?\.zip$")]
    private static partial Regex ZipName();

    private sealed class Day
    {
        public DateTime Date;
        public string Path = string.Empty;
        public bool IsZip;
        public bool HasErrors;
        public long Bytes;
    }

    public CleanupResult Run(DateTime? nowOverride = null)
    {
        DateTime now = nowOverride ?? DateTime.Now;
        var result = new CleanupResult { Time = now };
        if (!Directory.Exists(root))
        {
            return result;
        }

        DateTime protectFrom = EarliestActiveDay(now.Date);
        List<Day> days = Scan();

        // 1. Prošli dani u zip.
        foreach (Day d in days.Where(d => !d.IsZip && d.Date < protectFrom).ToList())
        {
            if (TryZip(d, out Day? zipped))
            {
                days.Remove(d);
                days.Add(zipped!);
                result.Zipped++;
            }
        }

        // 2. Zadržavanje po starosti.
        foreach (Day d in days.ToList())
        {
            double age = (now.Date - d.Date).TotalDays;
            if (d.Date >= protectFrom)
            {
                continue;
            }

            if (d.HasErrors)
            {
                if (age > options.KeepErrorDays)
                {
                    result.FreedBytes += Delete(d);
                    days.Remove(d);
                    result.DeletedDays++;
                }
                else if (age > options.KeepDays && d.IsZip && TryShrinkToErrors(d, out long freed))
                {
                    result.FreedBytes += freed;
                    result.ShrunkDays++;
                }
            }
            else if (age > options.KeepDays)
            {
                result.FreedBytes += Delete(d);
                days.Remove(d);
                result.DeletedDays++;
            }
        }

        // 3. Ograničenje ukupne veličine: prvo najstariji dani bez grešaka, zatim najstariji s greškama.
        long total = TotalSize();
        if (total > options.TargetBytes)
        {
            foreach (Day d in days.Where(d => d.Date < protectFrom).OrderBy(d => d.HasErrors).ThenBy(d => d.Date).ToList())
            {
                if (total <= options.TargetBytes)
                {
                    break;
                }

                long freed = Delete(d);
                total -= freed;
                result.FreedBytes += freed;
                result.DeletedDays++;
                days.Remove(d);
            }
        }

        // Preostalo prekoračenje zbog današnjih/aktivnih dana: ukloni rotirane datoteke (ne aktivnu).
        if (total > options.MaxBytes)
        {
            foreach (string f in Directory.EnumerateFiles(root, "*.log", SearchOption.AllDirectories)
                         .Where(f => RotatedFile().IsMatch(System.IO.Path.GetFileName(f)))
                         .OrderBy(File.GetLastWriteTimeUtc).ToList())
            {
                if (total <= options.MaxBytes)
                {
                    break;
                }

                long len = SafeLength(f);
                if (TryDeleteFile(f))
                {
                    total -= len;
                    result.FreedBytes += len;
                }
            }
        }

        RemoveEmptyFolders();
        result.TotalBytes = TotalSize();
        result.FreeDiskBytes = FreeDisk();
        WriteState(result);
        return result;
    }

    [GeneratedRegex(@"\.\d+\.log$")]
    private static partial Regex RotatedFile();

    public static CleanupResult? ReadState(string logsRoot)
    {
        try
        {
            string path = System.IO.Path.Combine(logsRoot, StateFile);
            return File.Exists(path) ? JsonSerializer.Deserialize<CleanupResult>(File.ReadAllText(path)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void WriteState(CleanupResult r)
    {
        try
        {
            File.WriteAllText(System.IO.Path.Combine(root, StateFile), JsonSerializer.Serialize(r));
        }
        catch (Exception)
        {
            // Nije bitno za rad.
        }
    }

    private long FreeDisk()
    {
        try
        {
            string? drive = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(root));
            return drive is { Length: > 0 } && !drive.StartsWith(@"\\", StringComparison.Ordinal) ? new DriveInfo(drive).AvailableFreeSpace : -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>Najraniji dan sesije koja je još živa (ili danas): ti dani se ne sažimaju ni brišu.</summary>
    private DateTime EarliestActiveDay(DateTime today)
    {
        DateTime earliest = today;
        foreach (string file in Directory.EnumerateFiles(root, "active-*.session"))
        {
            try
            {
                string[] parts = File.ReadAllText(file).Split('|');
                if (parts.Length >= 3 && DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset start)
                    && start.LocalDateTime.Date < earliest)
                {
                    earliest = start.LocalDateTime.Date;
                }
            }
            catch (Exception)
            {
                // Nečitljiv marker: smatra se aktivnim samo za danas.
            }
        }

        return earliest;
    }

    private List<Day> Scan()
    {
        var days = new List<Day>();
        foreach (string year in Directory.EnumerateDirectories(root).Where(d => Regex.IsMatch(System.IO.Path.GetFileName(d), @"^\d{4}$")))
        {
            foreach (string month in Directory.EnumerateDirectories(year).Where(d => Regex.IsMatch(System.IO.Path.GetFileName(d), @"^\d{2}$")))
            {
                foreach (string dir in Directory.EnumerateDirectories(month))
                {
                    string name = System.IO.Path.GetFileName(dir);
                    if (DayName().IsMatch(name) && DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                    {
                        days.Add(new Day { Date = date, Path = dir, HasErrors = DirHasSerious(dir), Bytes = DirSize(dir) });
                    }
                }

                foreach (string zip in Directory.EnumerateFiles(month, "*.zip"))
                {
                    Match m = ZipName().Match(System.IO.Path.GetFileName(zip));
                    if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                    {
                        days.Add(new Day { Date = date, Path = zip, IsZip = true, HasErrors = m.Groups[2].Success, Bytes = SafeLength(zip) });
                    }
                }
            }
        }

        return days;
    }

    private static bool DirHasSerious(string dir)
    {
        try
        {
            string errors = System.IO.Path.Combine(dir, "errors.log");
            if (!File.Exists(errors))
            {
                return false;
            }

            foreach (string line in File.ReadLines(errors))
            {
                if (line.Contains("\"lvl\":\"ERROR\"", StringComparison.Ordinal) || line.Contains("\"lvl\":\"CRITICAL\"", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            return true; // Nečitljivo: radije čuvaj.
        }

        return false;
    }

    private bool TryZip(Day d, out Day? zipped)
    {
        zipped = null;
        try
        {
            string suffix = d.HasErrors ? ".err.zip" : ".zip";
            string zipPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(d.Path)!, System.IO.Path.GetFileName(d.Path) + suffix);
            string tmp = zipPath + ".tmp";
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }

            using (ZipArchive zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                foreach (string f in Directory.EnumerateFiles(d.Path))
                {
                    zip.CreateEntryFromFile(f, System.IO.Path.GetFileName(f), CompressionLevel.Optimal);
                }
            }

            if (File.Exists(zipPath))
            {
                // Dan je već sažet ranije (npr. sesija se nastavila): novi sadržaj dobiva jedinstveno ime.
                zipPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(zipPath)!,
                    System.IO.Path.GetFileNameWithoutExtension(zipPath) + "-" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture) + ".zip");
            }

            File.Move(tmp, zipPath);
            Directory.Delete(d.Path, true);
            zipped = new Day { Date = d.Date, Path = zipPath, IsZip = true, HasErrors = d.HasErrors, Bytes = SafeLength(zipPath) };
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Nakon KeepDays dan s greškama čuva samo errors.log (s kontekstom oko grešaka).</summary>
    private static bool TryShrinkToErrors(Day d, out long freed)
    {
        freed = 0;
        try
        {
            long before = SafeLength(d.Path);
            using (ZipArchive zip = ZipFile.Open(d.Path, ZipArchiveMode.Update))
            {
                if (zip.Entries.All(e => e.Name.StartsWith("errors", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                foreach (ZipArchiveEntry e in zip.Entries.Where(e => !e.Name.StartsWith("errors", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    e.Delete();
                }
            }

            freed = Math.Max(0, before - SafeLength(d.Path));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static long Delete(Day d)
    {
        long size = d.IsZip ? SafeLength(d.Path) : DirSize(d.Path);
        try
        {
            if (d.IsZip)
            {
                File.Delete(d.Path);
            }
            else
            {
                Directory.Delete(d.Path, true);
            }

            return size;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static bool TryDeleteFile(string f)
    {
        try
        {
            File.Delete(f);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void RemoveEmptyFolders()
    {
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length).ToList())
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any() && Regex.IsMatch(System.IO.Path.GetFileName(dir), @"^(\d{4}|\d{2}|\d{4}-\d{2}-\d{2})$"))
                {
                    Directory.Delete(dir);
                }
            }
        }
        catch (Exception)
        {
            // Prazne mape nisu kritične.
        }
    }

    private long TotalSize() => DirSize(root);

    private static long DirSize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(SafeLength);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static long SafeLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
