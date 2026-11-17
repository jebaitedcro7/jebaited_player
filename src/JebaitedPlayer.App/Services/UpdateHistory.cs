using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.Services;

/// <summary>Bilješke jedne verzije na jednom jeziku.</summary>
public sealed class ReleaseNotesText
{
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("features")]
    public List<string> Features { get; init; } = [];

    [JsonPropertyName("improvements")]
    public List<string> Improvements { get; init; } = [];

    [JsonPropertyName("fixes")]
    public List<string> Fixes { get; init; } = [];

    [JsonPropertyName("known")]
    public List<string> Known { get; init; } = [];
}

/// <summary>Jedna verzija iz povijesti nadogradnji (ugrađena u aplikaciju).</summary>
public sealed class ReleaseEntry
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; init; } = string.Empty;

    [JsonPropertyName("notes")]
    public Dictionary<string, ReleaseNotesText> Notes { get; init; } = [];

    [JsonIgnore]
    public DateOnly ReleaseDate => DateOnly.ParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    [JsonIgnore]
    public Version ParsedVersion => UpdateHistory.ParseVersion(Version) ?? new Version(0, 0, 0);

    /// <summary>
    /// Bilješke za jezik sučelja. Povijest postoji na engleskom, njemačkom i hrvatskom; bosanski i srpski
    /// koriste hrvatsku, a ostali jezici englesku.
    /// </summary>
    public ReleaseNotesText NotesFor(string language)
    {
        foreach (string code in UpdateHistory.LanguageChain(language))
        {
            if (Notes.TryGetValue(code, out ReleaseNotesText? notes))
            {
                return notes;
            }
        }

        return Notes.Values.FirstOrDefault() ?? new ReleaseNotesText();
    }
}

/// <summary>Izdanje s GitHuba (novije od instaliranog), za prikaz kad je internet dostupan.</summary>
public sealed record OnlineRelease(string Version, string Name, DateTimeOffset? Published, string Body, string Url);

/// <summary>
/// Povijest nadogradnji (UPGRADE 3). Izvor je <c>History/update-history.json</c>, ugrađen u aplikaciju, pa povijest
/// radi bez interneta i svaka nova verzija donosi svoju (ažuriranu) povijest. Iz istog izvora se slažu CHANGELOG
/// datoteke i bilješke izdanja na GitHubu. GitHub se pita samo za verzije novije od instalirane (dodatna informacija).
/// </summary>
public static class UpdateHistory
{
    /// <summary>Jezici u kojima se piše povijest (ostali koriste jedan od njih).</summary>
    public static readonly IReadOnlyList<string> Languages = ["en", "de", "hr"];

    private static readonly Lazy<HistoryFile> File = new(LoadEmbedded);

    /// <summary>Sve verzije, najnovija prva.</summary>
    public static IReadOnlyList<ReleaseEntry> Versions => File.Value.Versions;

    /// <summary>GitHub repozitorij iz povijesti (isti kao za nadogradnje).</summary>
    public static string Repository => File.Value.Repository;

    public static ReleaseEntry? Find(string version)
    {
        Version? v = ParseVersion(version);
        return v is null ? null : Versions.FirstOrDefault(e => e.ParsedVersion == v);
    }

    /// <summary>Redoslijed jezika za bilješke: odabrani → srodni (bs, sr → hr) → engleski.</summary>
    public static IEnumerable<string> LanguageChain(string language)
    {
        yield return language;
        if (language is "bs" or "sr")
        {
            yield return "hr";
        }

        yield return "en";
    }

    /// <summary>"1.2.0", "v1.2.0" ili "1.2.0-beta" → 1.2.0 (null ako nije verzija).</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string t = text.Trim().TrimStart('v', 'V');
        int dash = t.IndexOfAny(['-', '+']);
        if (dash >= 0)
        {
            t = t[..dash];
        }

        return System.Version.TryParse(t, out Version? v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }

    /// <summary>Datum na jeziku sučelja, bez dana u tjednu (npr. "18. studenoga 2026." / "November 18, 2026").</summary>
    public static string FormatDate(ReleaseEntry entry) => FormatDate(entry.ReleaseDate.ToDateTime(TimeOnly.MinValue));

    public static string FormatDate(DateTime date)
    {
        string full = Loc.Culture.DateTimeFormat.LongDatePattern;
        string pattern = full.Replace("dddd", string.Empty).Trim(' ', ',', '.');
        if (full.EndsWith('.'))
        {
            pattern += ".";
        }

        return date.ToString(pattern, Loc.Culture);
    }

    /// <summary>
    /// GitHub: izdanja novija od <paramref name="currentVersion"/> (bez nacrta). Bez mreže ili kad GitHub ne odgovori
    /// vraća null; lokalna povijest se prikazuje i dalje.
    /// </summary>
    public static async Task<IReadOnlyList<OnlineRelease>?> FetchNewerAsync(string currentVersion, CancellationToken ct = default)
    {
        if (RepoPath() is not { } repo)
        {
            return null;
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("jebaited_player", AppPaths.Version.Split('+')[0]));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            string json = await http.GetStringAsync($"https://api.github.com/repos/{repo}/releases?per_page=30", ct).ConfigureAwait(false);
            return ParseGitHubReleases(json, currentVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Log.Info($"Povijest nadogradnji: GitHub nije dostupan ({ex.GetType().Name}), prikazuje se lokalna povijest.");
            return null;
        }
    }

    /// <summary>Odgovor GitHub API-ja (/releases) → izdanja novija od trenutne verzije, najnovije prvo.</summary>
    public static IReadOnlyList<OnlineRelease> ParseGitHubReleases(string json, string currentVersion)
    {
        Version current = ParseVersion(currentVersion) ?? new Version(0, 0, 0);
        var list = new List<(Version V, OnlineRelease R)>();
        using JsonDocument doc = JsonDocument.Parse(json);
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            if (e.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            string tag = e.TryGetProperty("tag_name", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;
            if (ParseVersion(tag) is not { } v || v <= current)
            {
                continue;
            }

            DateTimeOffset? published = e.TryGetProperty("published_at", out JsonElement p) && p.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(p.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset d)
                ? d
                : null;
            list.Add((v, new OnlineRelease(
                v.ToString(3),
                e.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? tag : tag,
                published,
                e.TryGetProperty("body", out JsonElement b) ? b.GetString() ?? string.Empty : string.Empty,
                e.TryGetProperty("html_url", out JsonElement u) ? u.GetString() ?? string.Empty : string.Empty)));
        }

        return list.OrderByDescending(x => x.V).Select(x => x.R).ToList();
    }

    /// <summary>
    /// CHANGELOG na zadanom jeziku (Keep a Changelog, Semantic Versioning): verzija, datum, sažetak, novo, poboljšanja,
    /// ispravci, poznati problemi i poveznica na usporedbu s prethodnom verzijom.
    /// </summary>
    public static string RenderChangelog(string language)
    {
        var t = ChangelogTexts.For(language);
        var sb = new StringBuilder();
        sb.Append("# ").Append(t.Title).Append("\n\n");
        sb.Append(t.Intro).Append("\n\n");
        sb.Append(t.Other).Append("\n\n");
        for (int i = 0; i < Versions.Count; i++)
        {
            ReleaseEntry e = Versions[i];
            ReleaseEntry? previous = i + 1 < Versions.Count ? Versions[i + 1] : null;
            sb.Append("## [").Append(e.Version).Append("] - ").Append(e.Date).Append("\n\n");
            AppendNotes(sb, e.NotesFor(language), t);
            sb.Append(previous is null
                    ? $"{t.Release}: [v{e.Version}]({Repository}/releases/tag/v{e.Version})"
                    : $"{t.Release}: [v{e.Version}]({Repository}/releases/tag/v{e.Version}) · {t.Compare} [v{previous.Version}...v{e.Version}]({Repository}/compare/v{previous.Version}...v{e.Version})")
                .Append("\n\n");
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>Bilješke izdanja za GitHub: engleski, njemački i hrvatski, datum i poveznica na prethodnu verziju.</summary>
    public static string RenderReleaseNotes(string version)
    {
        ReleaseEntry entry = Find(version) ?? throw new InvalidOperationException($"Version {version} is not in update-history.json.");
        int index = Versions.ToList().IndexOf(entry);
        ReleaseEntry? previous = index + 1 < Versions.Count ? Versions[index + 1] : null;
        var sb = new StringBuilder();
        sb.Append("**jebaited_player ").Append(entry.Version).Append("** · ").Append(entry.Date).Append("\n\n");
        foreach (string language in Languages)
        {
            var t = ChangelogTexts.For(language);
            sb.Append("## ").Append(t.LanguageName).Append("\n\n");
            AppendNotes(sb, entry.NotesFor(language), t);
        }

        sb.Append("---\n\n");
        if (previous != null)
        {
            sb.Append($"Previous version / Vorherige Version / Prethodna verzija: [v{previous.Version}]({Repository}/releases/tag/v{previous.Version})\n\n");
            sb.Append($"Full changelog: [v{previous.Version}...v{entry.Version}]({Repository}/compare/v{previous.Version}...v{entry.Version}) · ");
        }

        sb.Append("[CHANGELOG.md](").Append(Repository).Append("/blob/main/CHANGELOG.md) · [DE](")
            .Append(Repository).Append("/blob/main/CHANGELOG_DE.md) · [HR](").Append(Repository).Append("/blob/main/CHANGELOG_HR.md)\n");
        return sb.ToString();
    }

    private static void AppendNotes(StringBuilder sb, ReleaseNotesText notes, ChangelogTexts t)
    {
        if (notes.Summary.Length > 0)
        {
            sb.Append(notes.Summary).Append("\n\n");
        }

        Section(t.Features, notes.Features);
        Section(t.Improvements, notes.Improvements);
        Section(t.Fixes, notes.Fixes);
        Section(t.Known, notes.Known);

        void Section(string title, List<string> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            sb.Append("### ").Append(title).Append("\n\n");
            foreach (string item in items)
            {
                sb.Append("- ").Append(item).Append('\n');
            }

            sb.Append('\n');
        }
    }

    private static string? RepoPath()
    {
        string url = UpdateService.RepositoryUrl ?? Repository;
        const string prefix = "https://github.com/";
        return url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? url[prefix.Length..].Trim('/') : null;
    }

    private static HistoryFile LoadEmbedded()
    {
        using Stream stream = typeof(UpdateHistory).Assembly.GetManifestResourceStream("update-history.json")
                              ?? throw new InvalidOperationException("update-history.json is not embedded.");
        HistoryFile file = JsonSerializer.Deserialize<HistoryFile>(stream) ?? new HistoryFile();
        file.Versions.Sort((a, b) => b.ParsedVersion.CompareTo(a.ParsedVersion));
        return file;
    }

    private sealed class HistoryFile
    {
        [JsonPropertyName("repository")]
        public string Repository { get; init; } = string.Empty;

        [JsonPropertyName("versions")]
        public List<ReleaseEntry> Versions { get; init; } = [];

        [JsonPropertyName("changelog")]
        public Dictionary<string, ChangelogTexts> Changelog { get; init; } = [];
    }

    /// <summary>Naslovi u CHANGELOG datotekama (dokumentacija na GitHubu, ne sučelje playera); iz update-history.json.</summary>
    private sealed class ChangelogTexts
    {
        [JsonPropertyName("languageName")]
        public string LanguageName { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("intro")]
        public string Intro { get; init; } = string.Empty;

        [JsonPropertyName("other")]
        public string Other { get; init; } = string.Empty;

        [JsonPropertyName("features")]
        public string Features { get; init; } = string.Empty;

        [JsonPropertyName("improvements")]
        public string Improvements { get; init; } = string.Empty;

        [JsonPropertyName("fixes")]
        public string Fixes { get; init; } = string.Empty;

        [JsonPropertyName("known")]
        public string Known { get; init; } = string.Empty;

        [JsonPropertyName("release")]
        public string Release { get; init; } = string.Empty;

        [JsonPropertyName("compare")]
        public string Compare { get; init; } = string.Empty;

        public static ChangelogTexts For(string language) =>
            File.Value.Changelog.TryGetValue(language, out ChangelogTexts? t) ? t : File.Value.Changelog["en"];
    }
}
