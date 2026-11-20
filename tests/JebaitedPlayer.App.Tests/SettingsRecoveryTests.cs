using System.IO;
using System.Text.Json.Nodes;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Localization;

namespace JebaitedPlayer.App.Tests;

/// <summary>Ciklus 4, M3a: postavke preživljavaju update i oštećenu datoteku (nikad se ne prepisuju zadanim).</summary>
[TestClass]
public sealed class SettingsRecoveryTests
{
    private static readonly DateTime T0 = new(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
    private string _dir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "jp-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Settings => Path.Combine(_dir, "settings.json");

    private string[] CorruptFiles() => Directory.GetFiles(_dir, "settings.corrupt-*.json");

    /// <summary>Automatska kopija (M1a) s ispravnim postavkama, pa oštećena trenutna datoteka.</summary>
    private void BackupThenDamage(string good, string damaged, DateTime when)
    {
        File.WriteAllText(Settings, good);
        SettingsBackup.Create(_dir, null, "start", when);
        File.WriteAllText(Settings, damaged);
    }

    [TestMethod]
    public void Damaged_json_is_kept_aside_and_settings_come_back_from_the_newest_backup()
    {
        const string good = """{ "Volume": 37, "Theme": "light", "Language": "hr", "SeekStepSeconds": 15 }""";
        BackupThenDamage(good, """{ "Volume": 37, "Theme": "li""", T0);

        AppSettings s = AppSettings.Load(_dir, T0.AddHours(1));

        Assert.AreEqual(37, s.Volume);
        Assert.AreEqual("light", s.Theme);
        Assert.AreEqual(15, s.SeekStepSeconds);
        Assert.IsFalse(s.IsNew);
        Assert.AreEqual(SettingsLoadOutcome.RestoredFromBackup, s.LoadReport!.Outcome);
        Assert.AreEqual(T0, s.LoadReport.BackupCreatedUtc);

        // Oštećena datoteka je sačuvana netaknuta, a na mjestu settings.json je kopija.
        string corrupt = CorruptFiles().Single();
        Assert.AreEqual(s.LoadReport.CorruptFile, corrupt);
        Assert.AreEqual("""{ "Volume": 37, "Theme": "li""", File.ReadAllText(corrupt));
        Assert.AreEqual(good, File.ReadAllText(Settings));
    }

    [TestMethod]
    public void Empty_file_without_backup_gives_defaults_but_keeps_the_file()
    {
        File.WriteAllText(Settings, string.Empty);

        AppSettings s = AppSettings.Load(_dir, T0);

        Assert.AreEqual(100, s.Volume);
        Assert.IsTrue(s.IsNew);
        Assert.AreEqual(SettingsLoadOutcome.ResetToDefaults, s.LoadReport!.Outcome);
        Assert.AreEqual(string.Empty, File.ReadAllText(CorruptFiles().Single()));
        Assert.IsFalse(File.Exists(Settings));

        // Spremanje zadanih tada stvara novu datoteku (oštećena ostaje sa strane).
        s.Save();
        Assert.IsTrue(File.Exists(Settings));
        Assert.AreEqual(1, CorruptFiles().Length);
    }

    [TestMethod]
    public void Null_json_and_wrong_types_are_damaged_files_too()
    {
        foreach (string damaged in new[] { "null", "[1, 2]", """{ "Volume": "glasno" }""" })
        {
            Cleanup();
            Init();
            BackupThenDamage("""{ "Volume": 21, "Language": "de" }""", damaged, T0);

            AppSettings s = AppSettings.Load(_dir, T0.AddMinutes(5));

            Assert.AreEqual(21, s.Volume, damaged);
            Assert.AreEqual(SettingsLoadOutcome.RestoredFromBackup, s.LoadReport?.Outcome, damaged);
            Assert.AreEqual(damaged, File.ReadAllText(CorruptFiles().Single()), damaged);
        }
    }

    [TestMethod]
    public void A_damaged_newest_backup_is_skipped_for_an_older_good_one()
    {
        File.WriteAllText(Settings, """{ "Volume": 12, "Language": "hr" }""");
        SettingsBackup.Create(_dir, null, "start", T0);
        File.WriteAllText(Settings, "{ oštećeno");
        SettingsBackup.Create(_dir, null, "start", T0.AddDays(1));

        AppSettings s = AppSettings.Load(_dir, T0.AddDays(1).AddHours(1));

        Assert.AreEqual(12, s.Volume);
        Assert.AreEqual(T0, s.LoadReport!.BackupCreatedUtc);
    }

    [TestMethod]
    public void Old_file_without_version_loads_unchanged_and_saves_the_current_version()
    {
        // Datoteka iz 1.5.x: bez SettingsVersion i bez novijih polja.
        const string old = """{ "Volume": 37, "Theme": "dark", "SubtitleLanguages": ["hr", "en"], "Language": "hr" }""";
        File.WriteAllText(Settings, old);

        AppSettings s = AppSettings.Load(_dir, T0);

        Assert.IsNull(s.LoadReport);
        Assert.IsFalse(s.IsNew);
        Assert.AreEqual(37, s.Volume);
        CollectionAssert.AreEqual(new[] { "hr", "en" }, s.SubtitleLanguages);
        Assert.AreEqual(AppSettings.CurrentSettingsVersion, s.SettingsVersion);
        Assert.AreEqual(old, File.ReadAllText(Settings), "Učitavanje ne smije mijenjati datoteku.");

        s.Save();
        JsonObject saved = JsonNode.Parse(File.ReadAllText(Settings))!.AsObject();
        Assert.AreEqual(AppSettings.CurrentSettingsVersion, (int)saved["SettingsVersion"]!);
        Assert.AreEqual(37, (double)saved["Volume"]!);
        Assert.AreEqual(0, CorruptFiles().Length);
    }

    [TestMethod]
    public void Unknown_fields_from_a_newer_version_survive_a_save()
    {
        File.WriteAllText(Settings, """{ "SettingsVersion": 9, "Volume": 40, "FutureFeature": { "On": true, "Level": 3 } }""");

        AppSettings s = AppSettings.Load(_dir, T0);
        s.Volume = 41;
        s.Save();

        JsonObject saved = JsonNode.Parse(File.ReadAllText(Settings))!.AsObject();
        Assert.AreEqual(9, (int)saved["SettingsVersion"]!, "Novija oznaka oblika se ne smanjuje.");
        Assert.AreEqual(41, (double)saved["Volume"]!);
        Assert.AreEqual(3, (int)saved["FutureFeature"]!["Level"]!);
    }

    [TestMethod]
    public void A_file_that_cannot_be_read_is_never_overwritten()
    {
        const string content = """{ "Volume": 55 }""";
        File.WriteAllText(Settings, content);
        AppSettings s;
        using (new FileStream(Settings, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            s = AppSettings.Load(_dir, T0);
        }

        Assert.AreEqual(SettingsLoadOutcome.Unreadable, s.LoadReport!.Outcome);
        Assert.AreEqual(100, s.Volume);
        s.Volume = 10;
        s.Save();
        Assert.AreEqual(content, File.ReadAllText(Settings));
        Assert.AreEqual(0, CorruptFiles().Length);
    }

    [TestMethod]
    public void Read_only_load_for_registration_does_not_move_a_damaged_file()
    {
        File.WriteAllText(Settings, "{ nije json");

        AppSettings s = AppSettings.Load(_dir, T0, recover: false);
        s.Save();

        Assert.AreEqual("{ nije json", File.ReadAllText(Settings));
        Assert.AreEqual(0, CorruptFiles().Length);
    }

    [TestMethod]
    public void Settings_problem_messages_exist_in_every_language()
    {
        string previous = Loc.Current;
        var restored = new SettingsLoadReport(SettingsLoadOutcome.RestoredFromBackup, @"C:\x\settings.corrupt-1.json", T0, "bad");
        try
        {
            foreach (string language in new[] { "en", "de", "hr", "bs", "sr", "sl" })
            {
                Loc.SetLanguage(language);
                string text = restored.Describe();
                StringAssert.Contains(text, "settings.corrupt-1.json", language);
                Assert.IsFalse(text.Contains("settings.damaged", StringComparison.Ordinal), language);
                Assert.IsFalse((restored with { Outcome = SettingsLoadOutcome.ResetToDefaults }).Describe().Contains("settings.damaged", StringComparison.Ordinal), language);
                StringAssert.Contains((restored with { Outcome = SettingsLoadOutcome.Unreadable }).Describe(), "bad", language);
            }
        }
        finally
        {
            // Jezik je zajednički svim testovima (npr. UpdateTests očekuje hrvatski).
            Loc.SetLanguage(previous);
        }
    }
}
