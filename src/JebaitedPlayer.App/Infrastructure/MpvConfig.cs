using System.Globalization;
using JebaitedPlayer.Mpv;
using JebaitedPlayer.Mpv.Native;
using JebaitedPlayer.Subtitles;

namespace JebaitedPlayer.Infrastructure;

/// <summary>Zadane mpv postavke za jebaited_player.</summary>
internal static class MpvConfig
{
    public static readonly (string Name, string Value)[] BaseOptions =
    [
        // Prikaz i hardversko dekodiranje (D3D11 + NVDEC/D3D11VA bez kopiranja kroz CPU).
        ("vo", "gpu-next"),
        ("gpu-api", "d3d11"),
        ("hwdec", "auto-safe"),

        // Ponašanje reprodukcije.
        ("idle", "yes"),
        ("keep-open", "yes"),
        ("force-window", "no"),

        // Snimke zaslona (M6): format se određuje po nastavku datoteke (.jpg), kvaliteta 95.
        ("screenshot-jpeg-quality", "95"),

        // Sučelje i ulaz radi aplikacija, ne mpv.
        ("osc", "no"),
        ("input-default-bindings", "no"),
        ("input-vo-keyboard", "no"),
        ("input-cursor", "no"),
        ("cursor-autohide", "no"),
        ("drag-and-drop", "no"),

        // Bez skripti i vanjskih programa (sigurnost i manja potrošnja).
        ("ytdl", "no"),
        ("load-scripts", "no"),
        ("load-stats-overlay", "no"),
        ("load-console", "no"),
        ("load-auto-profiles", "no"),
        ("load-select", "no"),
        ("load-positioning", "no"),
        ("load-commands", "no"),
        ("load-context-menu", "no"),

        // Kratke poruke na videu (glasnoća, premotavanje), bez mpv-ove trake.
        ("osd-level", "1"),
        ("osd-bar", "no"),
        ("osd-font", "Segoe UI"),
        ("osd-font-size", "32"),
        ("osd-border-size", "2"),
        ("osd-duration", "1200"),
        ("osd-margin-x", "32"),
        ("osd-margin-y", "28"),

        // Vanjske titlove učitava aplikacija: prepozna encoding i preda mpv-u UTF-8 kopiju.
        ("sub-auto", "no"),
        ("sub-codepage", "utf-8"),
        ("audio-file-auto", "no"),

        // Ugrađeni titlovi: ako nema nijednog jezika s popisa prioriteta, ne prikazuj ništa.
        ("subs-fallback", "no"),

        ("volume-max", "100"),
        ("background-color", "#000000"),
    ];

    public static readonly (string Name, MpvFormat Format)[] ObservedProperties =
    [
        ("pause", MpvFormat.Flag),
        ("duration", MpvFormat.Double),
        ("volume", MpvFormat.Double),
        ("mute", MpvFormat.Flag),
        ("media-title", MpvFormat.String),
        ("idle-active", MpvFormat.Flag),
        ("eof-reached", MpvFormat.Flag),
        ("time-pos", MpvFormat.Double),
        ("track-list", MpvFormat.String),
        ("sid", MpvFormat.String),
        ("sub-delay", MpvFormat.Double),
        ("sub-speed", MpvFormat.Double),
        ("sub-visibility", MpvFormat.Flag),
        ("sub-start", MpvFormat.Double),
        ("aid", MpvFormat.String),
        ("speed", MpvFormat.Double),
        ("paused-for-cache", MpvFormat.Flag),
    ];

    // Nazivi nekih opcija razlikuju se među verzijama mpv-a; stvarne nazive otkrivamo pri pokretanju.
    private static string _outlineSizeOption = "sub-outline-size";
    private static string _outlineColorOption = "sub-outline-color";
    private static string _backgroundBoxValue = "background-box";

    public static string LanguagePriority(AppSettings settings) => SubtitleLanguage.BuildMpvSlang(settings.SubtitleLanguages);

    // --- UPGRADE 3: zvuk i slika iz postavki -----------------------------------------------------------------------

    /// <summary>Ujednačavanje glasnoće (dynaudnorm): tiši glasni prizori, glasniji dijalog; bez rezanja vrhova.</summary>
    public const string NormalizeGraph = "dynaudnorm=f=250:g=15:p=0.9:m=8";

    public const string NormalizeFilter = $"lavfi=[{NormalizeGraph}]";

    /// <summary>
    /// Cijeli audio filtar (mpv "af"): prvo equalizer, zatim ujednačavanje glasnoće, u jednom lavfi grafu.
    /// Prazno = bez filtara (zvuk točno kao u datoteci).
    /// </summary>
    public static string AudioFilter(AppSettings s)
    {
        var parts = new List<string>();
        if (Equalizer.BuildFilters(MusicMode ? s.MusicEqualizer : s.Equalizer) is { } eq)
        {
            parts.Add(eq);
        }

        if (s.NormalizeAudio)
        {
            parts.Add(NormalizeGraph);
        }

        string filter = parts.Count == 0 ? string.Empty : $"lavfi=[{string.Join(',', parts)}]";
        if (VuMeter)
        {
            filter = (filter.Length > 0 ? filter + "," : string.Empty) + VuMeterFilter;
        }

        return filter;
    }

    /// <summary>
    /// Mini player (ciklus 2): mjerenje razine zvuka za VU metar, samo dok je mini player otvoren.
    /// Razina se čita iz "af-metadata/vu" (RMS po kanalu, dB); na kraju lanca, pa pokazuje i učinak equalizera.
    /// </summary>
    public const string VuMeterFilter = "@vu:lavfi=[astats=metadata=1:reset=1]";

    public static bool VuMeter { get; set; }

    /// <summary>Svira glazba ili radio (glazbeni equalizer); inače film ili serija (filmski equalizer).</summary>
    public static bool MusicMode { get; set; }

    /// <summary>Opcije koje mijenja kvaliteta slike ("balanced" = zadane vrijednosti mpv-a).</summary>
    private static readonly string[] QualityOptionNames =
        ["scale", "cscale", "dscale", "dither", "correct-downscaling", "linear-downscaling", "sigmoid-upscaling", "hdr-compute-peak", "deband"];

    private static readonly Dictionary<string, string> QualityDefaults = [];

    private static IEnumerable<(string Name, string Value)> QualityValues(string quality) => quality switch
    {
        // Kao mpv-ov profil "high-quality": oštrije skaliranje i uklanjanje "stepenica" u gradijentima (više GPU-a).
        "high" => [("scale", "ewa_lanczossharp"), ("cscale", "ewa_lanczossharp"), ("dscale", "mitchell"), ("deband", "yes")],

        // Kao mpv-ov profil "fast": najjednostavnije skaliranje, za slabija računala.
        "fast" =>
        [
            ("scale", "bilinear"), ("cscale", "bilinear"), ("dscale", "bilinear"), ("dither", "no"), ("correct-downscaling", "no"),
            ("linear-downscaling", "no"), ("sigmoid-upscaling", "no"), ("hdr-compute-peak", "no"), ("deband", "no"),
        ],
        _ => [],
    };

    /// <summary>Opcije zvuka i slike iz postavki, prije mpv_initialize (zadane vrijednosti se ne postavljaju).</summary>
    public static void ApplyPlaybackOptionsBeforeInit(MpvPlayer player, AppSettings s)
    {
        foreach (var (name, value) in PlaybackOptions(s, includeDefaults: false))
        {
            if (!player.TrySetOption(name, value))
            {
                Log.Warn($"mpv opcija iz postavki nije prihvaćena: {name}={value}");
            }
        }
    }

    /// <summary>Nakon mpv_initialize: zapamti zadane vrijednosti opcija kvalitete (za povratak na "balanced" tijekom rada).</summary>
    public static void CaptureDefaults(MpvPlayer player)
    {
        foreach (string name in QualityOptionNames)
        {
            if (player.GetPropertyString($"option-info/{name}/default-value") is { } value)
            {
                QualityDefaults[name] = value;
            }
        }
    }

    /// <summary>Tijekom rada: primijeni postavke zvuka i slike odmah (promjena u Postavkama).</summary>
    public static void ApplyPlaybackLive(MpvPlayer player, AppSettings s)
    {
        foreach (var (name, value) in PlaybackOptions(s, includeDefaults: true))
        {
            player.SetPropertyAsync(name, value);
        }
    }

    /// <summary>mpv opcije za postavke zvuka i slike. S <paramref name="includeDefaults"/> i one koje vraćaju zadano stanje.</summary>
    public static IEnumerable<(string Name, string Value)> PlaybackOptions(AppSettings s, bool includeDefaults)
    {
        if (!s.HardwareDecoding || includeDefaults)
        {
            yield return ("hwdec", s.HardwareDecoding ? "auto-safe" : "no");
        }

        if (s.AudioLanguage is { Length: > 0 } lang)
        {
            yield return ("alang", SubtitleLanguage.BuildMpvSlang([lang]));
        }
        else if (includeDefaults)
        {
            yield return ("alang", string.Empty);
        }

        if (s.AudioDevice != "auto" || includeDefaults)
        {
            yield return ("audio-device", s.AudioDevice);
        }

        if (Math.Abs(s.AudioDelayMs) > 0.5 || includeDefaults)
        {
            yield return ("audio-delay", F(s.AudioDelayMs / 1000.0));
        }

        string audioFilter = AudioFilter(s);
        if (audioFilter.Length > 0 || includeDefaults)
        {
            yield return ("af", audioFilter);
        }

        if (s.HdrPassthrough || includeDefaults)
        {
            yield return ("target-colorspace-hint", s.HdrPassthrough ? "yes" : "no");
        }

        Dictionary<string, string> chosen = QualityValues(s.VideoQuality).ToDictionary(v => v.Name, v => v.Value);
        foreach (string name in QualityOptionNames)
        {
            if (chosen.TryGetValue(name, out string? value))
            {
                yield return (name, value);
            }
            else if (includeDefaults && QualityDefaults.TryGetValue(name, out string? def))
            {
                yield return (name, def);
            }
        }
    }

    /// <summary>Prije mpv_initialize: otkrij nazive opcija i postavi jezik i izgled titlova.</summary>
    public static void ApplySubtitleOptionsBeforeInit(MpvPlayer player, AppSettings settings)
    {
        player.TrySetOption("slang", LanguagePriority(settings));

        if (!player.TrySetOption("sub-outline-size", "2"))
        {
            _outlineSizeOption = "sub-border-size";
            _outlineColorOption = "sub-border-color";
        }

        if (!player.TrySetOption("sub-border-style", "background-box"))
        {
            _backgroundBoxValue = "opaque-box";
        }

        foreach (var (name, value) in StyleOptions(settings.SubtitleStyle))
        {
            if (!player.TrySetOption(name, value))
            {
                Log.Warn($"mpv opcija titlova nije prihvaćena: {name}={value}");
            }
        }
    }

    /// <summary>Tijekom rada: primijeni izgled titlova odmah.</summary>
    public static void ApplyStyle(MpvPlayer player, SubtitleStyleSettings style)
    {
        foreach (var (name, value) in StyleOptions(style))
        {
            player.SetPropertyAsync(name, value);
        }
    }

    private static IEnumerable<(string Name, string Value)> StyleOptions(SubtitleStyleSettings s)
    {
        yield return ("sub-font", s.FontName);
        yield return ("sub-font-size", F(s.FontSize));
        yield return ("sub-color", s.Color);
        yield return ("sub-bold", s.Bold ? "yes" : "no");
        yield return ("sub-pos", F(Math.Round(s.Position)));
        yield return (_outlineSizeOption, F(s.OutlineSize));
        yield return (_outlineColorOption, "#000000");
        yield return ("sub-border-style", s.BackgroundBox ? _backgroundBoxValue : "outline-and-shadow");
        yield return ("sub-back-color", "#A0000000");
        yield return ("sub-shadow-offset", s.BackgroundBox ? "0" : "1");
    }

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
