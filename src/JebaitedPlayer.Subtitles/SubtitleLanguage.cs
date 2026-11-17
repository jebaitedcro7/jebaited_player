using System.Text.RegularExpressions;

namespace JebaitedPlayer.Subtitles;

/// <summary>Jezik titlova: iz oznake (MKV/mpv), iz naziva datoteke i iz samog teksta.</summary>
public static partial class SubtitleLanguage
{
    private static readonly Dictionary<string, string> Tokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hr"] = "hr", ["hrv"] = "hr", ["cro"] = "hr", ["scr"] = "hr", ["croatian"] = "hr", ["hrvatski"] = "hr",
        ["sr"] = "sr", ["srp"] = "sr", ["scc"] = "sr", ["srb"] = "sr", ["serbian"] = "sr", ["srpski"] = "sr", ["srlat"] = "sr",
        ["bs"] = "bs", ["bos"] = "bs", ["bosnian"] = "bs", ["bosanski"] = "bs",
        ["sh"] = "sh", ["hbs"] = "sh", ["exyu"] = "sh",
        ["en"] = "en", ["eng"] = "en", ["english"] = "en", ["engleski"] = "en",
        ["sl"] = "sl", ["slv"] = "sl", ["slo"] = "sl", ["slovenian"] = "sl", ["slovenski"] = "sl",
        ["mk"] = "mk", ["mkd"] = "mk", ["mac"] = "mk", ["macedonian"] = "mk",
        ["de"] = "de", ["deu"] = "de", ["ger"] = "de", ["german"] = "de",
        ["fr"] = "fr", ["fra"] = "fr", ["fre"] = "fr", ["french"] = "fr",
        ["it"] = "it", ["ita"] = "it", ["italian"] = "it",
        ["es"] = "es", ["spa"] = "es", ["spanish"] = "es",
        ["hu"] = "hu", ["hun"] = "hu", ["hungarian"] = "hu",
    };

    private static readonly HashSet<string> EnglishWords =
    [
        "the", "and", "you", "that", "what", "is", "are", "to", "of", "it", "this", "have", "not", "with", "for",
        "your", "we", "he", "she", "was", "my", "me", "be", "do", "know", "just", "they", "there", "here", "about",
        "can", "will", "would", "all", "yes", "get", "go", "don", "him", "her", "were", "been", "right", "come",
    ];

    private static readonly HashSet<string> SouthSlavicWords =
    [
        "je", "da", "se", "ne", "sam", "si", "smo", "ste", "su", "ali", "za", "od", "ja", "ti", "mi", "vi", "kako",
        "ovo", "nije", "bio", "biti", "sve", "sa", "li", "kad", "kada", "gdje", "gde", "što", "šta", "mene", "tebe",
        "nešto", "ništa", "sada", "sad", "samo", "imam", "idemo", "hajde", "dobro", "hvala", "molim", "zašto", "jer",
        "već", "još", "bilo", "može", "mogu", "hoću", "neću", "treba", "znam", "znaš", "ovdje", "ovde", "tako",
    ];

    private static readonly string[] IjekavianPrefixes =
    [
        "gdje", "ovdje", "ondje", "uvijek", "lijep", "vrijem", "dijet", "mlijek", "riječ", "pjesm", "mjest", "htio",
        "djevojk", "nedjelj", "sjedi", "bježi", "vjeruj", "vjerova", "cijel", "svijet", "bijel", "sjeti", "cvijet",
        "vidje", "živje", "volje", "hljeb", "lijev", "pjeva", "dje",
    ];

    private static readonly string[] EkavianPrefixes =
    [
        "gde", "ovde", "onde", "uvek", "lep", "vrem", "dete", "deca", "mlek", "reč", "pesm", "mest", "hteo",
        "devojk", "nedelj", "sedi", "beži", "veruj", "verova", "ceo", "cel", "beli", "bela", "belo", "seti", "cvet",
        "videt", "živet", "volet", "hleb", "levo", "peva",
    ];

    private static readonly string[] CroatianLexical =
    [
        "tko", "nitko", "netko", "itko", "svatko", "što", "kruh", "tisuć", "točno", "tjedan", "obitelj", "povijest",
        "uopće", "juha", "vlak", "siječ", "zrakoplov", "kazališt", "glazb", "sretan", "sretn", "zrak",
    ];

    private static readonly string[] SerbianBosnianLexical =
    [
        "šta", "ko", "niko", "neko", "iko", "svako", "hiljad", "tačno", "porodic", "istorij", "historij", "uopšte",
        "kafa", "kafu", "supa", "voz", "muzik", "srećan", "srećn", "sedmic",
    ];

    /// <summary>Jezici koje korisnik može dodati u popis prioriteta.</summary>
    public static IReadOnlyList<string> Selectable { get; } = ["hr", "sr", "bs", "sh", "en", "sl", "mk", "de", "fr", "it", "es", "hu"];

    public static string DisplayName(string? code) => code switch
    {
        "hr" => "Hrvatski",
        "sr" => "Srpski",
        "bs" => "Bosanski",
        "sh" => "Srpskohrvatski",
        "en" => "Engleski",
        "sl" => "Slovenski",
        "mk" => "Makedonski",
        "de" => "Njemački",
        "fr" => "Francuski",
        "it" => "Talijanski",
        "es" => "Španjolski",
        "hu" => "Mađarski",
        "zh" or "chi" or "zho" => "Kineski",
        "ja" or "jpn" => "Japanski",
        "ko" or "kor" => "Korejski",
        "ru" or "rus" => "Ruski",
        "pl" or "pol" => "Poljski",
        "cs" or "cze" or "ces" => "Češki",
        "sk" or "slk" or "slo" => "Slovački",
        "pt" or "por" => "Portugalski",
        "nl" or "dut" or "nld" => "Nizozemski",
        "ro" or "rum" or "ron" => "Rumunjski",
        "tr" or "tur" => "Turski",
        "el" or "gre" or "ell" => "Grčki",
        "ar" or "ara" => "Arapski",
        "sv" or "swe" => "Švedski",
        "da" or "dan" => "Danski",
        "no" or "nor" or "nob" => "Norveški",
        "fi" or "fin" => "Finski",
        "he" or "heb" => "Hebrejski",
        "uk" or "ukr" => "Ukrajinski",
        "bg" or "bul" => "Bugarski",
        "sq" or "alb" or "sqi" => "Albanski",
        "und" or null or "" => "Nepoznat jezik",
        _ => code,
    };

    /// <summary>Svodi oznaku iz MKV-a ili mpv-a (npr. "hrv", "scr", "eng", "en-US") na kratku oznaku ("hr", "en").</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        string main = code.Trim().Split('-', '_')[0];
        return Tokens.TryGetValue(main, out string? c) ? c : main.ToLowerInvariant();
    }

    /// <summary>Oznake koje mpv koristi za usporedbu jezika ugrađenih titlova (dvo- i troslovne).</summary>
    public static IEnumerable<string> MpvCodes(string code) => code switch
    {
        "hr" => ["hr", "hrv", "scr"],
        "sr" => ["sr", "srp", "scc"],
        "bs" => ["bs", "bos"],
        "sh" => ["sh", "hbs"],
        "en" => ["en", "eng"],
        "sl" => ["sl", "slv"],
        "mk" => ["mk", "mkd", "mac"],
        "de" => ["de", "deu", "ger"],
        "fr" => ["fr", "fra", "fre"],
        "it" => ["it", "ita"],
        "es" => ["es", "spa"],
        "hu" => ["hu", "hun"],
        _ => [code],
    };

    /// <summary>mpv "slang" iz popisa prioriteta. Srpskohrvatske oznake dodaje iza zadnjeg južnoslavenskog jezika.</summary>
    public static string BuildMpvSlang(IEnumerable<string> priority)
    {
        var list = priority.ToList();
        var codes = new List<string>();
        int lastSouthSlavic = list.FindLastIndex(c => c is "hr" or "sr" or "bs");
        for (int i = 0; i < list.Count; i++)
        {
            codes.AddRange(MpvCodes(list[i]));
            if (i == lastSouthSlavic && !list.Contains("sh"))
            {
                codes.AddRange(MpvCodes("sh"));
            }
        }

        return string.Join(",", codes.Distinct());
    }

    /// <summary>Jezik iz naziva datoteke, npr. "Film.2023.hr.srt", "Film.Croatian.srt", "Subs/2_English.srt".</summary>
    public static string? FromFileName(string subtitlePath, string? videoPath = null)
    {
        string name = Path.GetFileNameWithoutExtension(subtitlePath);
        if (videoPath != null)
        {
            string videoBase = Path.GetFileNameWithoutExtension(videoPath);
            if (name.StartsWith(videoBase, StringComparison.OrdinalIgnoreCase))
            {
                name = name[videoBase.Length..];
            }
        }

        string[] tokens = TokenSplit().Split(name).Where(t => t.Length > 0).ToArray();
        for (int i = tokens.Length - 1; i >= 0; i--)
        {
            if (Tokens.TryGetValue(tokens[i], out string? code))
            {
                return code;
            }
        }

        return null;
    }

    /// <summary>
    /// Jezik iz teksta titlova (hr, sr, bs, en ili null). Ćirilica → srpski; ekavica → srpski;
    /// ijekavica: "tko/što/tjedan/kruh" → hrvatski, "ko/šta/sedmica/hiljadu" → bosanski.
    /// </summary>
    public static string? DetectFromText(string text)
    {
        if (text.Length > 40_000)
        {
            text = text[..40_000];
        }

        int cyrillic = 0, latin = 0;
        foreach (char c in text)
        {
            if (c is >= 'Ѐ' and <= 'ӿ')
            {
                cyrillic++;
            }
            else if (char.IsLetter(c))
            {
                latin++;
            }
        }

        if (cyrillic > 50 && cyrillic > latin)
        {
            return "sr";
        }

        var words = Word().Matches(text.ToLowerInvariant()).Select(m => m.Value).Take(8000).ToList();
        if (words.Count < 20)
        {
            return null;
        }

        int english = words.Count(EnglishWords.Contains);
        int slavic = words.Count(SouthSlavicWords.Contains);
        if (english >= 10 && english > slavic * 1.5)
        {
            return "en";
        }

        if (slavic < 8)
        {
            return null;
        }

        int ijekavian = words.Count(w => IjekavianPrefixes.Any(p => w.StartsWith(p, StringComparison.Ordinal)));
        int ekavian = words.Count(w => EkavianPrefixes.Any(p => w.StartsWith(p, StringComparison.Ordinal)));
        int croatian = words.Count(w => CroatianLexical.Any(m => w == m || (m.Length >= 4 && w.StartsWith(m, StringComparison.Ordinal))));
        int serbianBosnian = words.Count(w => SerbianBosnianLexical.Any(m => w == m || (m.Length >= 4 && w.StartsWith(m, StringComparison.Ordinal))));

        if (ekavian >= 3 && ekavian > ijekavian * 1.5)
        {
            return "sr";
        }

        if (croatian > serbianBosnian)
        {
            return "hr";
        }

        if (serbianBosnian > croatian)
        {
            return ijekavian >= ekavian ? "bs" : "sr";
        }

        return "hr";
    }

    [GeneratedRegex(@"[\s._\-\[\]\(\)]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenSplit();

    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();
}
