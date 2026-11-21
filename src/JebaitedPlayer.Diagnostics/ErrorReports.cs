using System.Text;
using System.Text.Json;

namespace JebaitedPlayer.Diagnostics;

/// <summary>
/// Ciklus 6, N1: jedan redigirani izvještaj o grešci ili padu. Bez putanja do mapa, imena korisnika, ključeva i IP adrese;
/// samo ono što pomaže popraviti grešku (verzija, inačica, Windows, vrsta, poruka, prvi redovi iznimke).
/// </summary>
public sealed record ErrorReport(string Id, DateTime CreatedUtc, string AppVersion, string Edition, string Os, string Level,
    string Category, string Type, string Component, string Message, string? Exception)
{
    public static ErrorReport Create(LogLevel level, string category, string type, string component, string message, Exception? ex,
        string appVersion, string edition, DateTime nowUtc) =>
        new(Guid.NewGuid().ToString("N")[..12], nowUtc, appVersion, edition, Environment.OSVersion.VersionString, level.ToString(),
            category, type, component, Trim(Redactor.Clean(message), 500),
            ex is null ? null : Trim(Redactor.Clean(string.Join("\n", ex.ToString().Split('\n').Take(12).Select(l => l.TrimEnd()))), 3000));

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

/// <summary>
/// N1: lokalni red izvještaja (mapa reports\ u logovima). Najviše 20 izvještaja i 2 MB, stariji od 14 dana se brišu.
/// Isti izvještaj (vrsta i poruka) zapisuje se najviše jednom po sesiji, a najviše 5 po sesiji.
/// </summary>
public sealed class ErrorReportQueue
{
    public const int MaxReports = 20;
    public const long MaxBytes = 2 * 1024 * 1024;
    public const int MaxPerSession = 5;
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly HashSet<string> _seen = [];
    private readonly object _gate = new();

    public ErrorReportQueue(string folder) => Folder = folder;

    public string Folder { get; }

    /// <summary>Dodaj izvještaj; vraća false ako je isti već zapisan u ovoj sesiji ili je dosegnut broj po sesiji.</summary>
    public bool Add(ErrorReport report, DateTime nowUtc)
    {
        lock (_gate)
        {
            string key = report.Type + "|" + report.Message;
            if (!_seen.Add(key) || _seen.Count > MaxPerSession)
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, $"{report.CreatedUtc:yyyyMMdd-HHmmssfff}_{report.Id}.json"), JsonSerializer.Serialize(report, Json));
                Prune(nowUtc);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>Izvještaji koji čekaju slanje, najstariji prvi.</summary>
    public IReadOnlyList<(string File, ErrorReport Report)> Pending()
    {
        lock (_gate)
        {
            if (!Directory.Exists(Folder))
            {
                return [];
            }

            var list = new List<(string, ErrorReport)>();
            foreach (string file in Directory.GetFiles(Folder, "*.json").Order(StringComparer.Ordinal))
            {
                try
                {
                    if (JsonSerializer.Deserialize<ErrorReport>(File.ReadAllText(file)) is { } r)
                    {
                        list.Add((file, r));
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    TryDelete(file);
                }
            }

            return list;
        }
    }

    public void Remove(string file)
    {
        lock (_gate)
        {
            TryDelete(file);
        }
    }

    /// <summary>Brisanje starih i viška (najstariji prvi) da red ne raste.</summary>
    public void Prune(DateTime nowUtc)
    {
        lock (_gate)
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            List<FileInfo> files = new DirectoryInfo(Folder).GetFiles("*.json").OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
            foreach (FileInfo f in files.Where(f => nowUtc - f.LastWriteTimeUtc > MaxAge).ToList())
            {
                TryDelete(f.FullName);
                files.Remove(f);
            }

            while (files.Count > MaxReports || files.Sum(f => f.Length) > MaxBytes)
            {
                TryDelete(files[0].FullName);
                files.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Dok nema prijemne točke: pripremljen GitHub issue (korisnik ga sam pregleda i pošalje). Najviše 5 izvještaja i
    /// otprilike 6.000 znakova, da adresa ostane valjana u pregledniku.
    /// </summary>
    public string GitHubIssueUrl(string repository, string appVersion)
    {
        IReadOnlyList<(string File, ErrorReport Report)> pending = Pending();
        var body = new StringBuilder();
        body.AppendLine($"Diagnostic report from jebaited_player {appVersion} (redacted: no folders, names or keys).");
        body.AppendLine();
        foreach ((_, ErrorReport r) in pending.TakeLast(5))
        {
            body.AppendLine($"### {r.CreatedUtc:yyyy-MM-dd HH:mm} UTC · {r.Level} · {r.Category}/{r.Type}");
            body.AppendLine($"Version {r.AppVersion} ({r.Edition}), {r.Os}, component {r.Component}");
            body.AppendLine($"Message: {r.Message}");
            if (r.Exception is { Length: > 0 } ex)
            {
                body.AppendLine("```");
                body.AppendLine(string.Join("\n", ex.Split('\n').Take(6)));
                body.AppendLine("```");
            }

            body.AppendLine();
        }

        string text = body.Length > 6000 ? body.ToString(0, 6000) + "\n…" : body.ToString();
        return $"{repository.TrimEnd('/')}/issues/new?title={Uri.EscapeDataString($"Diagnostic report {appVersion}")}&body={Uri.EscapeDataString(text)}";
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
