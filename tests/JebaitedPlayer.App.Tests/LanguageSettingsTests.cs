using System.Text.Json;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.App.Tests;

/// <summary>Jezik i tema u postavkama: stari settings.json (1.0.x) se učitava, novi jezik se pamti.</summary>
[TestClass]
public sealed class LanguageSettingsTests
{
    [TestMethod]
    public void Settings_from_version_1_0_have_no_language_and_keep_everything_else()
    {
        const string old = """
            { "Volume": 70, "Theme": "light", "SubtitleLanguages": ["hr", "en"], "AutoPlayNext": false }
            """;
        AppSettings s = JsonSerializer.Deserialize<AppSettings>(old)!;
        Assert.IsNull(s.Language, "Bez jezika se pri pokretanju ponudi odabir.");
        Assert.AreEqual("light", s.Theme);
        Assert.AreEqual(70, s.Volume);
        CollectionAssert.AreEqual(new[] { "hr", "en" }, s.SubtitleLanguages);
        Assert.IsFalse(s.AutoPlayNext);
        Assert.IsFalse(s.IsNew, "Postojeće postavke nisu nova instalacija.");
    }

    [TestMethod]
    public void Language_and_theme_survive_save_and_load()
    {
        var s = new AppSettings { Language = "de", Theme = "dark" };
        AppSettings loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        Assert.AreEqual("de", loaded.Language);
        Assert.AreEqual("dark", loaded.Theme);
        StringAssert.DoesNotMatch(JsonSerializer.Serialize(s), new System.Text.RegularExpressions.Regex("IsNew"), "Zastavica se ne sprema.");
    }

    [TestMethod]
    public void New_install_defaults_are_dark_theme_and_no_language()
    {
        var s = new AppSettings();
        Assert.AreEqual("dark", s.Theme);
        Assert.IsNull(s.Language);
    }

    [DataTestMethod]
    [DataRow("en", "en")]
    [DataRow("de", "de,en")]
    [DataRow("hr", "hr,sr,bs,en")]
    [DataRow("bs", "bs,hr,sr,en")]
    [DataRow("sr", "sr,hr,bs,en")]
    [DataRow("sl", "sl,hr,en")]
    public void Subtitle_languages_follow_the_chosen_language_on_a_new_install(string ui, string expected) =>
        Assert.AreEqual(expected, string.Join(",", AppSettings.DefaultSubtitleLanguages(ui)));
}
