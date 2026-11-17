using System.Globalization;
using System.Text.RegularExpressions;

namespace JebaitedPlayer.Library;

/// <summary>Što se može pročitati iz naziva datoteke (i mape) filma ili epizode.</summary>
public sealed record MediaInfo
{
    public string FilePath { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int? Year { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public int? EpisodeEnd { get; init; }
    public string? EpisodeTitle { get; init; }
    public string? Resolution { get; init; }
    public string? Source { get; init; }
    public string? VideoCodec { get; init; }
    public string? ReleaseGroup { get; init; }

    public bool IsEpisode => Episode.HasValue;

    /// <summary>"S03E07", "S01E01-E02" ili "E07" (bez sezone).</summary>
    public string EpisodeCode
    {
        get
        {
            if (Episode is not int e)
            {
                return string.Empty;
            }

            string code = Season is int s ? $"S{s:00}E{e:00}" : $"E{e:00}";
            return EpisodeEnd is int end && end != e ? $"{code}-E{end:00}" : code;
        }
    }

    public string DisplayTitle => IsEpisode
        ? $"{Title} · {EpisodeCode}{(EpisodeTitle is { Length: > 0 } t ? " · " + t : string.Empty)}"
        : Year is int y ? $"{Title} ({y})" : Title;
}

/// <summary>
/// Prepoznaje naziv, godinu, sezonu/epizodu, rezoluciju, izvor, kodek i release grupu iz naziva datoteke.
/// Podržava scene nazive (<c>Show.S03E07.1080p.WEB-DL.x264-NTb</c>), <c>1x07</c>, "Season 3 Episode 7",
/// više epizoda (<c>S01E01E02</c>), anime (<c>[Grupa] Show - 07 (1080p)</c>) i sezonu iz naziva mape.
/// </summary>
public static partial class MediaNameParser
{
    private static readonly HashSet<string> NotReleaseGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "dl", "rip", "hd", "sd", "x264", "x265", "h264", "h265", "hevc", "avc", "aac", "ac3", "dts", "1080p", "720p",
        "2160p", "480p", "web", "bluray", "hdtv", "dvdrip", "webrip", "remux", "proper", "repack", "hdr", "sdr", "10bit",
        "8bit", "en", "hr", "eng", "sub", "subs", "dub", "multi", "final", "cut", "edition", "part", "cd1", "cd2",
    };

    public static MediaInfo Parse(string path)
    {
        string raw = Path.GetFileNameWithoutExtension(path);
        string? directory = Path.GetDirectoryName(path);
        string? folder = directory is null ? null : Path.GetFileName(directory);
        string? parentFolder = directory is null ? null : Path.GetFileName(Path.GetDirectoryName(directory));

        string cleaned = TrailingBrackets().Replace(LeadingBrackets().Replace(raw, string.Empty), string.Empty);
        string norm = Spaces().Replace(Separators().Replace(cleaned, " "), " ").Trim();

        int? season = null, episode = null, episodeEnd = null;
        int markerStart = -1, markerEnd = -1;
        Match m;
        if ((m = SeasonEpisode().Match(norm)).Success)
        {
            season = Int(m, 1);
            episode = Int(m, 2);
            episodeEnd = m.Groups[3].Success ? Int(m, 3) : null;
        }
        else if ((m = CrossFormat().Match(norm)).Success || (m = LongFormat().Match(norm)).Success)
        {
            season = Int(m, 1);
            episode = Int(m, 2);
        }
        else if ((m = EpisodeOnly().Match(norm)).Success || (m = AbsoluteEpisode().Match(norm)).Success)
        {
            episode = Int(m, 1);
            season = SeasonFromFolder(folder);
        }

        if (m.Success)
        {
            markerStart = m.Index;
            markerEnd = m.Index + m.Length;
        }

        // Godina: zadnja godina prije oznake epizode koja nije na samom početku ("1917 2019" → 2019, naslov "1917").
        int? year = null;
        int yearIndex = -1;
        foreach (Match y in Year().Matches(norm))
        {
            if (y.Index > 0 && (markerStart < 0 || y.Index < markerStart))
            {
                year = int.Parse(y.Value, CultureInfo.InvariantCulture);
                yearIndex = y.Index;
            }
        }

        Match quality = Quality().Match(norm);
        int qualityIndex = quality.Success ? quality.Index : -1;

        int titleEnd = new[] { markerStart, yearIndex, qualityIndex, norm.Length }.Where(i => i >= 0).Min();
        string title = CleanTitle(norm[..titleEnd]);
        if (title.Length == 0)
        {
            title = TitleFromFolders(folder, parentFolder) ?? CleanTitle(norm);
        }

        string? episodeTitle = null;
        if (markerEnd >= 0)
        {
            int end = qualityIndex > markerEnd ? qualityIndex : norm.Length;
            string rest = CleanTitle(norm[markerEnd..end]);
            if (rest.Length > 1 && rest.Any(char.IsLetter) && rest.Length <= 80)
            {
                episodeTitle = rest;
            }
        }

        return new MediaInfo
        {
            FilePath = path,
            Title = title,
            Year = year,
            Season = season,
            Episode = episode,
            EpisodeEnd = episodeEnd,
            EpisodeTitle = episodeTitle,
            Resolution = Map(Resolution().Match(norm), v => v.ToLowerInvariant() is "4k" or "uhd" ? "2160p" : v.ToLowerInvariant()),
            Source = Map(Source().Match(norm), NormalizeSource),
            VideoCodec = Map(Codec().Match(norm), NormalizeCodec),
            ReleaseGroup = ReleaseGroup(raw),
        };
    }

    /// <summary>Usporedivi oblik naslova (mala slova, bez interpunkcije) za grupiranje epizoda iste serije.</summary>
    public static string NormalizeTitle(string title) =>
        NonAlphanumeric().Replace(title.ToLowerInvariant(), string.Empty);

    private static string? TitleFromFolders(string? folder, string? parentFolder)
    {
        string? source = SeasonFromFolder(folder) != null ? parentFolder : folder;
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        string norm = Spaces().Replace(Separators().Replace(source, " "), " ").Trim();
        int cut = norm.Length;
        foreach (Match y in Year().Matches(norm))
        {
            if (y.Index > 0)
            {
                cut = Math.Min(cut, y.Index);
            }
        }

        Match q = Quality().Match(norm);
        if (q.Success)
        {
            cut = Math.Min(cut, q.Index);
        }

        string title = CleanTitle(norm[..cut]);
        return title.Length > 0 ? title : null;
    }

    /// <summary>Je li mapa sezona ("Season 1", "S01", "Sezona 2"…).</summary>
    public static bool IsSeasonFolder(string? folderName) => SeasonFromFolder(folderName).HasValue;

    private static int? SeasonFromFolder(string? folder)
    {
        if (folder is null)
        {
            return null;
        }

        Match m = SeasonFolder().Match(folder.Trim());
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static string? ReleaseGroup(string raw)
    {
        Match leading = LeadingGroup().Match(raw);
        Match trailing = TrailingGroup().Match(raw);
        if (trailing.Success && !NotReleaseGroups.Contains(trailing.Groups[1].Value))
        {
            return trailing.Groups[1].Value;
        }

        return leading.Success ? leading.Groups[1].Value.Trim() : null;
    }

    private static string CleanTitle(string text) =>
        Spaces().Replace(text, " ").Trim().Trim('-', '–', '(', '[', '{', ' ', ',').Trim();

    private static string NormalizeSource(string value) => value.ToUpperInvariant().Replace("-", string.Empty) switch
    {
        "BLURAY" or "BDRIP" or "BRRIP" or "BDREMUX" or "REMUX" => "BluRay",
        "WEBDL" or "WEB" => "WEB-DL",
        "WEBRIP" => "WEBRip",
        "HDTV" => "HDTV",
        "DVDRIP" or "DVD" => "DVD",
        "HDRIP" => "HDRip",
        _ => value,
    };

    private static string NormalizeCodec(string value) => value.ToUpperInvariant().Replace(" ", string.Empty) switch
    {
        "X264" or "H264" or "AVC" => "H.264",
        "X265" or "H265" or "HEVC" => "HEVC",
        "AV1" => "AV1",
        "XVID" => "XviD",
        "VP9" => "VP9",
        _ => value,
    };

    private static string? Map(Match m, Func<string, string> map) => m.Success ? map(m.Groups[1].Value) : null;

    private static int Int(Match m, int group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"^(\[[^\]]*\]\s*)+", Opt)]
    private static partial Regex LeadingBrackets();

    [GeneratedRegex(@"(\s*\[[^\]]*\])+$", Opt)]
    private static partial Regex TrailingBrackets();

    [GeneratedRegex(@"^\[([^\]]+)\]", Opt)]
    private static partial Regex LeadingGroup();

    [GeneratedRegex(@"-([A-Za-z0-9]{2,20})(?:\[[^\]]*\])?$", Opt)]
    private static partial Regex TrailingGroup();

    [GeneratedRegex(@"[._]+", Opt)]
    private static partial Regex Separators();

    [GeneratedRegex(@"\s{2,}", Opt)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[^\p{L}\p{N}]+", Opt)]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"\bS(\d{1,2})\s?E(\d{1,3})(?:(?:\s?-\s?E?|E)(\d{1,3}))?(?!\d)", Opt)]
    private static partial Regex SeasonEpisode();

    [GeneratedRegex(@"\b(\d{1,2})x(\d{2,3})\b", Opt)]
    private static partial Regex CrossFormat();

    [GeneratedRegex(@"\b(?:Season|Sezona|Series)\s?(\d{1,2})\s?[,-]?\s?(?:Episode|Epizoda|Ep)\s?(\d{1,3})\b", Opt)]
    private static partial Regex LongFormat();

    [GeneratedRegex(@"\b(?:(?:Episode|Epizoda|Ep)\s?|E)(\d{1,3})\b", Opt)]
    private static partial Regex EpisodeOnly();

    [GeneratedRegex(@"\s-\s(\d{1,3})(?:v\d)?(?=\s|$|\()", Opt)]
    private static partial Regex AbsoluteEpisode();

    [GeneratedRegex(@"^(?:Season|Sezona|Series|Staffel|S)[ ._-]?(\d{1,2})$", Opt)]
    private static partial Regex SeasonFolder();

    [GeneratedRegex(@"\b(19[2-9]\d|20[0-4]\d)\b", Opt)]
    private static partial Regex Year();

    [GeneratedRegex(@"\b(?:2160p|1080[pi]|720p|576p|480p|4K|UHD|Blu-?Ray|BDRip|BRRip|BDRemux|REMUX|WEB-?DL|WEB-?Rip|WEB|HDTV|DVDRip|DVD|HDRip|x26[45]|H\s?26[45]|HEVC|AV1|XviD|PROPER|REPACK|EXTENDED|UNRATED|10bit|HDR10|HDR|IMAX)\b", Opt)]
    private static partial Regex Quality();

    [GeneratedRegex(@"\b(2160p|4K|UHD|1080[pi]|720p|576p|480p)\b", Opt)]
    private static partial Regex Resolution();

    [GeneratedRegex(@"\b(Blu-?Ray|BDRip|BRRip|BDRemux|REMUX|WEB-?DL|WEB-?Rip|WEB|HDTV|DVDRip|DVD|HDRip)\b", Opt)]
    private static partial Regex Source();

    [GeneratedRegex(@"\b(x264|x265|H\s?264|H\s?265|HEVC|AVC|AV1|XviD|VP9)\b", Opt)]
    private static partial Regex Codec();
}
