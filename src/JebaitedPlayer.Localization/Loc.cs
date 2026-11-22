using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace JebaitedPlayer.Localization;

/// <summary>Jezik iz popisa: kod (npr. "hr"), naziv na tom jeziku ("Hrvatski") i na engleskom ("Croatian").</summary>
public sealed record LanguageInfo(string Code, string Name, string EnglishName)
{
    public bool IsPrimary => Loc.PrimaryCodes.Contains(Code);

    public override string ToString() => Name;
}

/// <summary>
/// Tekstovi sučelja. Svaki jezik je jedna datoteka <c>Languages/&lt;kod&gt;.json</c> (ugrađena u program):
/// ravni popis "ključ": "tekst" i zaglavlje <c>_language.*</c> (naziv, kultura za brojeve, pravilo množine).
/// Novi jezik = kopija <c>en.json</c> s prijevodom, bez izmjene koda.
/// <para>
/// Engleski je izvor i rezerva: tekst koji nedostaje u odabranom jeziku uzima se iz engleskog, a ako ni tamo
/// ne postoji, prikazuje se ključ (vidljivo u testovima, nikad prazno). Učitavaju se samo odabrani jezik i engleski.
/// </para>
/// <para>
/// Brojevi u tekstovima formatiraju se prema jeziku (<see cref="Culture"/>), ali kultura procesa se ne mijenja:
/// čitanje titlova, vremena i vrijednosti iz mpv-a ostaje neovisno o jeziku sučelja.
/// </para>
/// </summary>
public static class Loc
{
    public const string DefaultLanguage = "en";

    /// <summary>Glavni jezici, uvijek vidljivi i ovim redom. Ostali idu u "More languages".</summary>
    public static readonly IReadOnlyList<string> PrimaryCodes = ["en", "de", "hr"];

    private const string ResourcePrefix = "lang.";
    private const string ResourceSuffix = ".json";

    private static readonly Assembly Self = typeof(Loc).Assembly;
    private static readonly Lazy<IReadOnlyList<LanguageInfo>> LanguageList = new(Discover);
    private static readonly Pack Fallback = Pack.Load(DefaultLanguage);
    private static volatile Pack _current = Fallback;

    /// <summary>Promjena jezika (poziva se na dretvi koja je pozvala <see cref="SetLanguage"/>).</summary>
    public static event Action? LanguageChanged;

    /// <summary>Kod trenutnog jezika ("en", "de", "hr"…).</summary>
    public static string Current => _current.Code;

    /// <summary>Kultura trenutnog jezika, samo za formatiranje brojeva i datuma u tekstovima.</summary>
    public static CultureInfo Culture => _current.Culture;

    /// <summary>Svi ugrađeni jezici: glavni (EN, DE, HR) prvi, zatim ostali po nazivu.</summary>
    public static IReadOnlyList<LanguageInfo> Available => LanguageList.Value;

    public static bool IsSupported(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Available.Any(l => l.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Kod ako je jezik podržan, inače engleski.</summary>
    public static string Normalize(string? code) =>
        IsSupported(code) ? code!.Trim().ToLowerInvariant() : DefaultLanguage;

    public static void SetLanguage(string? code)
    {
        string normalized = Normalize(code);
        if (normalized == _current.Code)
        {
            return;
        }

        _current = normalized == DefaultLanguage ? Fallback : Pack.Load(normalized);
        LanguageChanged?.Invoke();
    }

    /// <summary>Tekst za ključ (odabrani jezik → engleski → sam ključ).</summary>
    public static string T(string key) =>
        _current.Texts.TryGetValue(key, out string? text) || Fallback.Texts.TryGetValue(key, out text) ? text : key;

    /// <summary>Tekst s parametrima {0}, {1}… (brojevi prema jeziku).</summary>
    public static string T(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <summary>
    /// Tekst u ispravnom obliku množine: ključevi <c>key.one</c>, <c>key.few</c>, <c>key.other</c>… prema pravilu jezika.
    /// Bez dodatnih parametara broj je {0}.
    /// </summary>
    public static string Plural(string key, long count, params object?[] args)
    {
        Pack pack = _current;
        string? template = pack.Texts.GetValueOrDefault($"{key}.{PluralRules.Form(pack.PluralRule, count)}")
                           ?? pack.Texts.GetValueOrDefault($"{key}.other");
        CultureInfo culture = pack.Culture;
        if (template is null)
        {
            template = Fallback.Texts.GetValueOrDefault($"{key}.{PluralRules.Form(Fallback.PluralRule, count)}")
                       ?? Fallback.Texts.GetValueOrDefault($"{key}.other")
                       ?? key;
        }

        return string.Format(culture, template, args.Length == 0 ? [count] : args);
    }

    /// <summary>Svi tekstovi jednog jezika (za testove i provjeru prijevoda).</summary>
    public static IReadOnlyDictionary<string, string> LoadAll(string code) => Pack.Read(code);

    private static IReadOnlyList<LanguageInfo> Discover()
    {
        var list = new List<LanguageInfo>();
        foreach (string resource in Self.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !resource.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            string code = resource[ResourcePrefix.Length..^ResourceSuffix.Length].ToLowerInvariant();
            Dictionary<string, string> header = Pack.ReadHeader(code);
            list.Add(new LanguageInfo(
                code,
                header.GetValueOrDefault("_language.name") ?? code,
                header.GetValueOrDefault("_language.englishName") ?? code));
        }

        return
        [
            .. PrimaryCodes.Select(c => list.FirstOrDefault(l => l.Code == c)).OfType<LanguageInfo>(),
            .. list.Where(l => !l.IsPrimary).OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    private sealed class Pack
    {
        private Pack(string code, FrozenDictionary<string, string> texts)
        {
            Code = code;
            Texts = texts;
            PluralRule = texts.GetValueOrDefault("_language.plural") ?? PluralRules.OneOther;
            Culture = ToCulture(texts.GetValueOrDefault("_language.culture"));
        }

        public string Code { get; }

        public FrozenDictionary<string, string> Texts { get; }

        public string PluralRule { get; }

        public CultureInfo Culture { get; }

        public static Pack Load(string code) => new(code, Read(code).ToFrozenDictionary(StringComparer.Ordinal));

        public static Dictionary<string, string> Read(string code)
        {
            using Stream? stream = Self.GetManifestResourceStream(ResourcePrefix + code + ResourceSuffix);
            if (stream is null)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            Dictionary<string, string> texts = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                                               ?? new Dictionary<string, string>(StringComparer.Ordinal);

            // Satelitski tekstovi (langx.<modul>.<kod>.json), npr. Torlink u GitHub inačici; u Store izdanju ih nema.
            string suffix = "." + code + ResourceSuffix;
            foreach (string name in Self.GetManifestResourceNames())
            {
                if (!name.StartsWith("langx.", StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    continue;
                }

                using Stream? extra = Self.GetManifestResourceStream(name);
                if (extra is null || JsonSerializer.Deserialize<Dictionary<string, string>>(extra) is not { } more)
                {
                    continue;
                }

                foreach ((string k, string v) in more)
                {
                    texts[k] = v;
                }
            }

            return texts;
        }

        /// <summary>Samo zaglavlje (_language.*) s početka datoteke, bez učitavanja svih tekstova.</summary>
        public static Dictionary<string, string> ReadHeader(string code)
        {
            var header = new Dictionary<string, string>(StringComparer.Ordinal);
            using Stream? stream = Self.GetManifestResourceStream(ResourcePrefix + code + ResourceSuffix);
            if (stream is null)
            {
                return header;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var reader = new Utf8JsonReader(memory.GetBuffer().AsSpan(0, (int)memory.Length));
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                string name = reader.GetString() ?? string.Empty;
                if (!name.StartsWith("_language.", StringComparison.Ordinal))
                {
                    break;
                }

                reader.Read();
                header[name] = reader.GetString() ?? string.Empty;
            }

            return header;
        }

        private static CultureInfo ToCulture(string? name)
        {
            try
            {
                return string.IsNullOrWhiteSpace(name) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(name);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }
}
