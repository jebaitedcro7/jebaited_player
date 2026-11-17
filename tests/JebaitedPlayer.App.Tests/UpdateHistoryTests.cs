using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using JebaitedPlayer.Services;
using JebaitedPlayer.Views;

namespace JebaitedPlayer.App.Tests;

/// <summary>
/// Povijest nadogradnji (UPGRADE 3): ugrađena povijest je potpuna i ispravna, CHANGELOG datoteke odgovaraju joj,
/// a odgovor GitHuba se ispravno filtrira. Povijest mora raditi bez interneta (ugrađena je u program).
/// </summary>
[TestClass]
public sealed partial class UpdateHistoryTests
{
    private static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "jebaited_player.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("Korijen repozitorija nije pronađen.");
        }
    }

    [TestMethod]
    public void History_is_embedded_and_has_every_public_version()
    {
        string[] versions = UpdateHistory.Versions.Select(v => v.Version).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "1.0.0", "1.0.1", "1.1.0" }, versions);
        Assert.AreEqual(versions.Length, versions.Distinct().Count(), "Svaka verzija samo jednom.");
        Assert.AreEqual("https://github.com/jebaitedcro7/jebaited_player", UpdateHistory.Repository);
    }

    [TestMethod]
    public void Newest_version_in_history_is_the_version_being_built()
    {
        string props = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));
        string built = VersionRegex().Match(props).Groups[1].Value;
        Assert.AreEqual(built, UpdateHistory.Versions[0].Version,
            "Nova verzija mora imati zapis u History/update-history.json (datum, novosti, poboljšanja, ispravke, poznati problemi).");
    }

    [TestMethod]
    public void Versions_are_semantic_newest_first_with_valid_dates()
    {
        Version? previous = null;
        DateOnly? previousDate = null;
        foreach (ReleaseEntry e in UpdateHistory.Versions)
        {
            Assert.IsTrue(SemVerRegex().IsMatch(e.Version), $"{e.Version}: MAJOR.MINOR.PATCH");
            Assert.IsTrue(DateOnly.TryParseExact(e.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date), e.Version);
            if (previous != null)
            {
                Assert.IsTrue(e.ParsedVersion < previous, $"{e.Version} mora biti iza {previous}");
                Assert.IsTrue(date <= previousDate, $"{e.Version}: datum ne smije biti noviji od novije verzije");
            }

            previous = e.ParsedVersion;
            previousDate = date;
        }
    }

    [TestMethod]
    public void Every_version_is_written_in_english_german_and_croatian()
    {
        foreach (ReleaseEntry e in UpdateHistory.Versions)
        {
            foreach (string lang in UpdateHistory.Languages)
            {
                Assert.IsTrue(e.Notes.ContainsKey(lang), $"{e.Version}: nema jezika {lang}");
                ReleaseNotesText n = e.Notes[lang];
                Assert.IsFalse(string.IsNullOrWhiteSpace(n.Summary), $"{e.Version} {lang}: sažetak");
                Assert.IsTrue(n.Features.Count + n.Improvements.Count + n.Fixes.Count > 0, $"{e.Version} {lang}: nema promjena");
                Assert.IsTrue(n.Features.Concat(n.Improvements).Concat(n.Fixes).Concat(n.Known).All(s => s.Trim().Length > 0), $"{e.Version} {lang}: prazna stavka");
            }

            // Isti broj stavki u svakom jeziku (prijevod, ne drugi sadržaj).
            ReleaseNotesText en = e.Notes["en"];
            foreach (string lang in new[] { "de", "hr" })
            {
                ReleaseNotesText t = e.Notes[lang];
                Assert.AreEqual(en.Features.Count, t.Features.Count, $"{e.Version} {lang}: novosti");
                Assert.AreEqual(en.Improvements.Count, t.Improvements.Count, $"{e.Version} {lang}: poboljšanja");
                Assert.AreEqual(en.Fixes.Count, t.Fixes.Count, $"{e.Version} {lang}: ispravke");
                Assert.AreEqual(en.Known.Count, t.Known.Count, $"{e.Version} {lang}: poznati problemi");
            }
        }
    }

    [TestMethod]
    public void Other_languages_use_a_related_history()
    {
        ReleaseEntry e = UpdateHistory.Versions[0];
        Assert.AreSame(e.Notes["hr"], e.NotesFor("bs"));
        Assert.AreSame(e.Notes["hr"], e.NotesFor("sr"));
        Assert.AreSame(e.Notes["en"], e.NotesFor("sl"));
        Assert.AreSame(e.Notes["de"], e.NotesFor("de"));
    }

    [DataTestMethod]
    [DataRow("1.2.0", "1.2.0")]
    [DataRow("v1.10.3", "1.10.3")]
    [DataRow("1.2.0-beta.1", "1.2.0")]
    [DataRow("2.0", "2.0.0")]
    public void Versions_are_parsed(string text, string expected) =>
        Assert.AreEqual(Version.Parse(expected), UpdateHistory.ParseVersion(text));

    [TestMethod]
    public void Invalid_version_is_null() => Assert.IsNull(UpdateHistory.ParseVersion("latest"));

    /// <summary>
    /// CHANGELOG.md, CHANGELOG_DE.md i CHANGELOG_HR.md nastaju iz iste povijesti. Uz JEBAITED_WRITE_CHANGELOG=1 test ih
    /// zapiše (tools\update-changelog.ps1), inače provjerava da su ažurni.
    /// </summary>
    [DataTestMethod]
    [DataRow("en", "CHANGELOG.md")]
    [DataRow("de", "CHANGELOG_DE.md")]
    [DataRow("hr", "CHANGELOG_HR.md")]
    public void Changelog_files_match_the_history(string language, string file)
    {
        string expected = UpdateHistory.RenderChangelog(language);
        string path = Path.Combine(Root, file);
        if (Environment.GetEnvironmentVariable("JEBAITED_WRITE_CHANGELOG") == "1")
        {
            File.WriteAllText(path, expected, new System.Text.UTF8Encoding(false));
        }

        Assert.IsTrue(File.Exists(path), file);
        Assert.AreEqual(expected, File.ReadAllText(path).Replace("\r\n", "\n"), $"{file} nije ažuran: pokreni tools\\update-changelog.ps1");
        foreach (ReleaseEntry e in UpdateHistory.Versions)
        {
            StringAssert.Contains(expected, $"## [{e.Version}] - {e.Date}");
        }
    }

    [TestMethod]
    public void Changelog_links_every_version_to_the_previous_one()
    {
        string md = UpdateHistory.RenderChangelog("en");
        for (int i = 0; i + 1 < UpdateHistory.Versions.Count; i++)
        {
            string now = UpdateHistory.Versions[i].Version, before = UpdateHistory.Versions[i + 1].Version;
            StringAssert.Contains(md, $"/compare/v{before}...v{now}");
        }

        StringAssert.Contains(md, "/releases/tag/v1.0.0");
    }

    [TestMethod]
    public void Release_notes_have_three_languages_date_and_previous_version()
    {
        string notes = UpdateHistory.RenderReleaseNotes("1.1.0");
        StringAssert.Contains(notes, "## English");
        StringAssert.Contains(notes, "## Deutsch");
        StringAssert.Contains(notes, "## Hrvatski");
        StringAssert.Contains(notes, "2026-11-17");
        StringAssert.Contains(notes, "/releases/tag/v1.0.1");
        StringAssert.Contains(notes, "/compare/v1.0.1...v1.1.0");
    }

    /// <summary>
    /// Bilješke izdanja za GitHub (tools\release-notes.ps1 → release.ps1 i GitHub Actions): uz JEBAITED_RELEASE_NOTES_VERSION
    /// i JEBAITED_RELEASE_NOTES_OUT zapiše ih u datoteku.
    /// </summary>
    [TestMethod]
    public void Release_notes_file_for_the_release_script()
    {
        string version = Environment.GetEnvironmentVariable("JEBAITED_RELEASE_NOTES_VERSION") ?? UpdateHistory.Versions[0].Version;
        string notes = UpdateHistory.RenderReleaseNotes(version);
        Assert.IsTrue(notes.Length > 300, "Bilješke su prekratke.");
        if (Environment.GetEnvironmentVariable("JEBAITED_RELEASE_NOTES_OUT") is { Length: > 0 } output)
        {
            File.WriteAllText(output, notes, new System.Text.UTF8Encoding(false));
        }
    }

    [TestMethod]
    public void GitHub_answer_gives_only_newer_published_versions_newest_first()
    {
        const string json = """
            [
              { "tag_name": "v1.4.0", "name": "jebaited_player 1.4.0", "draft": true, "published_at": null, "body": "x", "html_url": "https://github.com/a/b/releases/tag/v1.4.0" },
              { "tag_name": "v1.2.1", "name": "jebaited_player 1.2.1", "draft": false, "published_at": "2026-12-01T10:00:00Z", "body": "**fix**", "html_url": "https://github.com/a/b/releases/tag/v1.2.1" },
              { "tag_name": "v1.3.0", "name": "jebaited_player 1.3.0", "draft": false, "published_at": "2026-12-20T10:00:00Z", "body": "new", "html_url": "https://github.com/a/b/releases/tag/v1.3.0" },
              { "tag_name": "v1.2.0", "name": "jebaited_player 1.2.0", "draft": false, "published_at": "2026-11-18T10:00:00Z", "body": "", "html_url": "" },
              { "tag_name": "v1.0.0", "name": "old", "draft": false, "published_at": "2026-11-17T10:00:00Z", "body": "", "html_url": "" }
            ]
            """;
        IReadOnlyList<OnlineRelease> newer = UpdateHistory.ParseGitHubReleases(json, "1.2.0");
        CollectionAssert.AreEqual(new[] { "1.3.0", "1.2.1" }, newer.Select(r => r.Version).ToArray());
        Assert.AreEqual(new DateTimeOffset(2026, 12, 20, 10, 0, 0, TimeSpan.Zero), newer[0].Published);
    }

    [TestMethod]
    public void Markdown_from_GitHub_is_shown_as_plain_text()
    {
        string text = UpdateHistoryWindow.PlainText("## English\n\n**jebaited_player** · [v1.1.0](https://x/y)\n- one\n- `two`\n\n---\n");
        Assert.AreEqual("English\n\njebaited_player · v1.1.0\n• one\n• two", text);
    }

    [GeneratedRegex(@"<Version>([^<]+)</Version>")]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex SemVerRegex();
}
