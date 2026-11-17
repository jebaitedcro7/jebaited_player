using System.Globalization;
using System.Text.RegularExpressions;

namespace JebaitedPlayer.Localization.Tests;

/// <summary>Datoteke jezika: isti ključevi, isti parametri, ispravna množina, zaglavlje.</summary>
[TestClass]
public sealed partial class LanguageFileTests
{
    private static readonly string[] PluralForms = ["one", "two", "few", "other"];

    [TestMethod]
    public void Primary_languages_come_first_in_order()
    {
        CollectionAssert.AreEqual(new[] { "en", "de", "hr" }, Loc.Available.Take(3).Select(l => l.Code).ToArray());
        Assert.IsTrue(Loc.Available.Count >= 6, "Uz glavne jezike postoje i dodatni (bs, sr, sl).");
    }

    [TestMethod]
    public void Every_language_has_a_valid_header()
    {
        foreach (LanguageInfo language in Loc.Available)
        {
            IReadOnlyDictionary<string, string> texts = Loc.LoadAll(language.Code);
            Assert.AreEqual(language.Code, texts["_language.code"], $"{language.Code}: kod u zaglavlju");
            Assert.IsFalse(string.IsNullOrWhiteSpace(texts["_language.name"]), $"{language.Code}: naziv");
            Assert.IsFalse(string.IsNullOrWhiteSpace(texts["_language.englishName"]), $"{language.Code}: engleski naziv");
            Assert.IsNotNull(CultureInfo.GetCultureInfo(texts["_language.culture"]), $"{language.Code}: kultura");
            CollectionAssert.Contains(
                new[] { PluralRules.OneOther, PluralRules.SouthSlavic, PluralRules.Slovenian, PluralRules.None },
                texts["_language.plural"], $"{language.Code}: pravilo množine");
        }
    }

    [TestMethod]
    public void Every_language_has_the_same_keys_as_english()
    {
        HashSet<string> english = Simple(Loc.LoadAll("en"));
        foreach (LanguageInfo language in Loc.Available.Where(l => l.Code != "en"))
        {
            HashSet<string> keys = Simple(Loc.LoadAll(language.Code));
            string[] missing = english.Except(keys).Order().ToArray();
            string[] extra = keys.Except(english).Order().ToArray();
            Assert.AreEqual(0, missing.Length, $"{language.Code} nema: {string.Join(", ", missing)}");
            Assert.AreEqual(0, extra.Length, $"{language.Code} ima višak: {string.Join(", ", extra)}");
        }
    }

    [TestMethod]
    public void Plural_keys_have_exactly_the_forms_of_the_language_rule()
    {
        HashSet<string> bases = PluralBases(Loc.LoadAll("en"));
        Assert.IsTrue(bases.Count > 0);
        foreach (LanguageInfo language in Loc.Available)
        {
            IReadOnlyDictionary<string, string> texts = Loc.LoadAll(language.Code);
            IReadOnlyList<string> forms = PluralRules.FormsOf(texts["_language.plural"]);
            foreach (string b in bases)
            {
                string[] present = PluralForms.Where(f => texts.ContainsKey($"{b}.{f}")).ToArray();
                CollectionAssert.AreEquivalent(forms.ToArray(), present, $"{language.Code}: {b}");
            }
        }
    }

    [TestMethod]
    public void No_text_is_empty()
    {
        foreach (LanguageInfo language in Loc.Available)
        {
            foreach ((string key, string value) in Loc.LoadAll(language.Code))
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"{language.Code}: {key} je prazan");
            }
        }
    }

    [TestMethod]
    public void Translations_use_the_same_placeholders_as_english()
    {
        IReadOnlyDictionary<string, string> english = Loc.LoadAll("en");
        foreach (LanguageInfo language in Loc.Available.Where(l => l.Code != "en"))
        {
            foreach ((string key, string value) in Loc.LoadAll(language.Code))
            {
                if (key.StartsWith('_'))
                {
                    continue;
                }

                string source = english.TryGetValue(key, out string? e) ? e : english[PluralBase(key) + ".other"];
                CollectionAssert.AreEquivalent(Placeholders(source), Placeholders(value), $"{language.Code}: {key} = \"{value}\"");
                _ = string.Format(CultureInfo.InvariantCulture, value, new object[10]);
            }
        }
    }

    [TestMethod]
    public void Croatian_and_German_special_letters_survive_loading()
    {
        Assert.IsTrue(Loc.LoadAll("hr").Values.Any(v => v.Contains('č') && v.Contains('ž')), "hr: č, ž");
        Assert.IsTrue(Loc.LoadAll("hr").Values.Any(v => v.Contains('đ')), "hr: đ");
        Assert.IsTrue(Loc.LoadAll("de").Values.Any(v => v.Contains('ä') || v.Contains('ö') || v.Contains('ü')), "de: umlauti");
        Assert.IsTrue(Loc.LoadAll("de").Values.Any(v => v.Contains('ß')), "de: ß");
    }

    internal static HashSet<string> Simple(IReadOnlyDictionary<string, string> texts)
    {
        HashSet<string> bases = PluralBases(texts);
        return texts.Keys.Where(k => !k.StartsWith('_') && !bases.Contains(PluralBase(k))).ToHashSet();
    }

    internal static HashSet<string> PluralBases(IReadOnlyDictionary<string, string> texts) =>
        texts.Keys.Where(k => k.EndsWith(".other", StringComparison.Ordinal)).Select(PluralBase).ToHashSet();

    private static string PluralBase(string key) => key[..key.LastIndexOf('.')];

    private static string[] Placeholders(string text) =>
        PlaceholderRegex().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order().ToArray();

    [GeneratedRegex(@"\{(\d+)(?:[:,][^}]*)?\}")]
    private static partial Regex PlaceholderRegex();
}

/// <summary>Ponašanje sustava: rezerva na engleski, promjena jezika, množina.</summary>
[TestClass]
[DoNotParallelize]
public sealed class LocBehaviourTests
{
    [TestCleanup]
    public void Reset() => Loc.SetLanguage("en");

    [TestMethod]
    public void Unknown_language_falls_back_to_english()
    {
        Assert.AreEqual("en", Loc.Normalize("xx"));
        Assert.AreEqual("en", Loc.Normalize(null));
        Assert.AreEqual("de", Loc.Normalize(" DE "));
        Loc.SetLanguage("xx");
        Assert.AreEqual("en", Loc.Current);
    }

    [TestMethod]
    public void Missing_key_shows_the_key_never_empty()
    {
        Loc.SetLanguage("hr");
        Assert.AreEqual("no.such.key", Loc.T("no.such.key"));
    }

    [TestMethod]
    public void Language_change_raises_event_and_changes_texts()
    {
        int raised = 0;
        void Handler() => raised++;
        Loc.LanguageChanged += Handler;
        try
        {
            Loc.SetLanguage("en");
            string english = Loc.T("settings.title");
            Loc.SetLanguage("de");
            Assert.AreEqual("de", Loc.Current);
            Assert.AreNotEqual(english, Loc.T("settings.title"));
            Loc.SetLanguage("de");
            Assert.AreEqual(1, raised, "Isti jezik ne javlja promjenu.");
        }
        finally
        {
            Loc.LanguageChanged -= Handler;
        }
    }

    [TestMethod]
    public void Settings_tooltip_is_translated_in_the_three_main_languages()
    {
        Loc.SetLanguage("en");
        Assert.AreEqual("Settings", Loc.T("home.settingsTooltip"));
        Loc.SetLanguage("de");
        Assert.AreEqual("Einstellungen", Loc.T("home.settingsTooltip"));
        Loc.SetLanguage("hr");
        Assert.AreEqual("Postavke", Loc.T("home.settingsTooltip"));
    }

    [TestMethod]
    public void Numbers_follow_the_language()
    {
        Loc.SetLanguage("hr");
        Assert.AreEqual("1,5", 1.5.ToString("0.0", Loc.Culture));
        Loc.SetLanguage("en");
        Assert.AreEqual("1.5", 1.5.ToString("0.0", Loc.Culture));
    }

    [TestMethod]
    public void Changing_language_does_not_change_the_process_culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        Loc.SetLanguage("de");
        Assert.AreEqual(before, CultureInfo.CurrentCulture);
    }

    [DataTestMethod]
    [DataRow(1, "one")]
    [DataRow(2, "few")]
    [DataRow(4, "few")]
    [DataRow(5, "other")]
    [DataRow(11, "other")]
    [DataRow(12, "other")]
    [DataRow(21, "one")]
    [DataRow(22, "few")]
    [DataRow(25, "other")]
    [DataRow(111, "other")]
    public void South_slavic_plural(int n, string form) => Assert.AreEqual(form, PluralRules.Form(PluralRules.SouthSlavic, n));

    [DataTestMethod]
    [DataRow(1, "one")]
    [DataRow(2, "two")]
    [DataRow(3, "few")]
    [DataRow(4, "few")]
    [DataRow(5, "other")]
    [DataRow(101, "one")]
    [DataRow(102, "two")]
    public void Slovenian_plural(int n, string form) => Assert.AreEqual(form, PluralRules.Form(PluralRules.Slovenian, n));

    [TestMethod]
    public void Plural_text_in_croatian_and_english()
    {
        Loc.SetLanguage("hr");
        Assert.AreEqual("1 titl", Loc.Plural("online.count", 1));
        Assert.AreEqual("3 titla", Loc.Plural("online.count", 3));
        Assert.AreEqual("5 titlova", Loc.Plural("online.count", 5));
        Assert.AreEqual("21 titl", Loc.Plural("online.count", 21));
        Loc.SetLanguage("en");
        Assert.AreEqual("1 subtitle", Loc.Plural("online.count", 1));
        Assert.AreEqual("3 subtitles", Loc.Plural("online.count", 3));
    }
}
