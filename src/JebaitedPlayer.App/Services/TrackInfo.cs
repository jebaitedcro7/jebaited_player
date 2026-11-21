using System.Globalization;
using System.Text.RegularExpressions;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.Services;

/// <summary>Podaci o pjesmi koje mpv zna (oznake datoteke i ICY oznaka radija).</summary>
internal sealed record TrackMeta(
    string? Artist, string? Title, string? Album, string? Year, string? Genre,
    string? Bitrate, string? SampleRate, string? FileName, string? Station, string? Icy, bool IsStream);

/// <summary>
/// 2.1.7: što se prikazuje u traci s podacima o pjesmi. Zadano samo izvođač i naziv pjesme; korisnik desnim klikom
/// uključuje album, godinu, žanr, kvalitetu zvuka i naziv datoteke. Postavka vrijedi za sve izglede mini playera i Home ekran.
/// </summary>
internal static partial class TrackInfo
{
    public const string Artist = "artist";
    public const string Title = "title";
    public const string Album = "album";
    public const string Year = "year";
    public const string Genre = "genre";
    public const string Quality = "quality";
    public const string File = "file";

    /// <summary>Svi dostupni podaci, redom kojim se nude u izborniku (ključ prijevoda "info.&lt;id&gt;").</summary>
    public static readonly IReadOnlyList<string> AllFields = [Artist, Title, Album, Year, Genre, Quality, File];

    public static readonly IReadOnlyList<string> DefaultFields = [Artist, Title];

    /// <summary>Samo poznati podaci, bez ponavljanja, u redoslijedu izbornika.</summary>
    public static List<string> Normalize(IEnumerable<string>? fields)
    {
        HashSet<string> wanted = (fields ?? DefaultFields).ToHashSet(StringComparer.Ordinal);
        return AllFields.Where(wanted.Contains).ToList();
    }

    /// <summary>Čita podatke iz mpv-a (blokira, pa se zove izvan UI dretve).</summary>
    public static TrackMeta Read(Func<string, string?> property, string? currentPath, string? title, bool isStream)
    {
        string? mediaTitle = property("media-title");
        string? year = property("metadata/by-key/Date") ?? property("metadata/by-key/Year");
        return new TrackMeta(
            Artist: property("metadata/by-key/Artist"),
            Title: mediaTitle,
            Album: property("metadata/by-key/Album"),
            Year: YearOf(year),
            Genre: property("metadata/by-key/Genre"),
            Bitrate: property("audio-bitrate"),
            SampleRate: property("audio-params/samplerate"),
            FileName: string.IsNullOrEmpty(currentPath) || isStream ? null : System.IO.Path.GetFileName(currentPath),
            Station: isStream ? (title is { Length: > 0 } ? title : mediaTitle) : null,
            Icy: isStream ? property("metadata/by-key/icy-title") : null,
            IsStream: isStream);
    }

    /// <summary>Tekst za traku: „Izvođač — Naslov”, a ostali odabrani podaci iza razmaka „···”.</summary>
    public static string Format(TrackMeta m, IReadOnlyCollection<string> fields, string? fallbackName = null)
    {
        bool artist = fields.Contains(Artist), title = fields.Contains(Title);
        if (!artist && !title)
        {
            title = true; // traka nikad ne ostaje prazna
        }

        string main;
        if (m.IsStream)
        {
            // Radio: „Postaja — trenutna pjesma” (ICY oznaka streama, ako je postaja šalje).
            main = m.Station ?? string.Empty;
            if (title && m.Icy is { Length: > 0 } song && !main.Contains(song, StringComparison.OrdinalIgnoreCase))
            {
                main = main.Length > 0 ? main + " — " + song : song;
            }
        }
        else
        {
            string name = m.Title is { Length: > 0 } t ? t : System.IO.Path.GetFileNameWithoutExtension(m.FileName ?? fallbackName ?? string.Empty);
            if (artist && m.Artist is { Length: > 0 } a)
            {
                main = title && !name.Contains(a, StringComparison.OrdinalIgnoreCase) ? a + " — " + name : title ? name : a;
            }
            else
            {
                main = name;
            }
        }

        var extra = new List<string>();
        if (!m.IsStream)
        {
            AddIf(extra, fields.Contains(Album), m.Album);
            AddIf(extra, fields.Contains(Year), m.Year);
            AddIf(extra, fields.Contains(Genre), m.Genre);
        }

        if (fields.Contains(Quality))
        {
            var quality = new List<string>();
            if (double.TryParse(m.Bitrate, NumberStyles.Float, CultureInfo.InvariantCulture, out double bps) && bps > 0)
            {
                quality.Add($"{Math.Round(bps / 1000):0} kbps");
            }

            if (double.TryParse(m.SampleRate, NumberStyles.Float, CultureInfo.InvariantCulture, out double hz) && hz > 0)
            {
                quality.Add((hz / 1000).ToString("0.#", CultureInfo.InvariantCulture) + " kHz");
            }

            if (quality.Count > 0)
            {
                extra.Add(string.Join(" · ", quality));
            }
        }

        AddIf(extra, fields.Contains(File), m.FileName);
        return extra.Count > 0 ? $"{main}  ···  {string.Join(" · ", extra)}" : main;
    }

    private static void AddIf(List<string> list, bool include, string? value)
    {
        if (include && !string.IsNullOrWhiteSpace(value))
        {
            list.Add(value.Trim());
        }
    }

    private static string? YearOf(string? date) =>
        date is null ? null : YearPattern().Match(date) is { Success: true } m ? m.Value : null;

    [GeneratedRegex(@"\d{4}")]
    private static partial Regex YearPattern();
}
