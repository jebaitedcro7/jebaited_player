using System.IO;
using System.Text.Json;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Library;
using Microsoft.Data.Sqlite;

namespace JebaitedPlayer.App.Tests;

/// <summary>Ciklus 2: automatske kopije (M1a) te izvoz i uvoz postavki (M1b).</summary>
[TestClass]
public sealed class SettingsBackupTests
{
    private static readonly DateTime T0 = new(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
    private string _dir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "jp-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Settings => Path.Combine(_dir, "settings.json");

    private WatchStore StoreWithHistory()
    {
        var store = new WatchStore(Path.Combine(_dir, "library.db"));
        store.SavePosition(@"D:\Filmovi\A.mkv", 600, 5400, watched: false);
        store.SavePosition(@"D:\Filmovi\B.mkv", 0, 5400, watched: true);
        return store;
    }

    // --- M1a ----------------------------------------------------------------------------------------------------

    [TestMethod]
    public void Daily_backup_copies_settings_and_a_consistent_database()
    {
        File.WriteAllText(Settings, """{ "Volume": 42, "Theme": "light", "Language": "hr" }""");
        using WatchStore store = StoreWithHistory();

        BackupInfo b = SettingsBackup.CreateIfDue(_dir, store, T0)!;
        Assert.AreEqual("start", b.Reason);
        Assert.AreEqual(File.ReadAllText(Settings), File.ReadAllText(Path.Combine(b.Folder, "settings.json")));

        // Kopija baze je potpuna i čitljiva, iako je izvorna baza otvorena (WAL).
        using var copy = new WatchStore(Path.Combine(b.Folder, "library.db"));
        Assert.AreEqual(600, copy.Get(@"D:\Filmovi\A.mkv")!.Position);
        Assert.IsTrue(copy.Get(@"D:\Filmovi\B.mkv")!.Watched);
    }

    [TestMethod]
    public void Only_one_start_backup_per_day()
    {
        File.WriteAllText(Settings, """{ "Volume": 42 }""");
        Assert.IsNotNull(SettingsBackup.CreateIfDue(_dir, null, T0));
        Assert.IsNull(SettingsBackup.CreateIfDue(_dir, null, T0.AddHours(5)));
        Assert.IsNull(SettingsBackup.CreateIfDue(_dir, null, T0.AddHours(23.9)));
        Assert.IsNotNull(SettingsBackup.CreateIfDue(_dir, null, T0.AddHours(24.1)));
        Assert.AreEqual(2, SettingsBackup.List(_dir).Count);
    }

    [TestMethod]
    public void Only_the_last_seven_are_kept_newest_first()
    {
        File.WriteAllText(Settings, """{ "Volume": 42 }""");
        for (int day = 0; day < 10; day++)
        {
            SettingsBackup.Create(_dir, null, day % 3 == 0 ? "update-1.4.0" : "start", T0.AddDays(day));
        }

        IReadOnlyList<BackupInfo> list = SettingsBackup.List(_dir);
        Assert.AreEqual(SettingsBackup.Keep, list.Count);
        Assert.AreEqual(T0.AddDays(9), list[0].Created);
        Assert.AreEqual(T0.AddDays(3), list[^1].Created);
        Assert.AreEqual("update-1.4.0", list.First(b => b.Created == T0.AddDays(9)).Reason);
    }

    [TestMethod]
    public void Before_update_backup_copies_the_closed_database_file()
    {
        File.WriteAllText(Settings, """{ "Volume": 42 }""");
        using (StoreWithHistory())
        {
        }

        SqliteConnection.ClearAllPools();
        BackupInfo b = SettingsBackup.Create(_dir, null, "update-1.4.0", T0)!;
        using var copy = new WatchStore(Path.Combine(b.Folder, "library.db"));
        Assert.IsTrue(copy.Get(@"D:\Filmovi\B.mkv")!.Watched);
    }

    [TestMethod]
    public void Nothing_to_back_up_on_a_fresh_install()
    {
        Assert.IsNull(SettingsBackup.CreateIfDue(_dir, null, T0));
        Assert.AreEqual(0, SettingsBackup.List(_dir).Count);
    }

    [TestMethod]
    public void Unfinished_backups_are_ignored_and_cleaned_up()
    {
        File.WriteAllText(Settings, """{ "Volume": 42 }""");
        Directory.CreateDirectory(Path.Combine(SettingsBackup.BackupsDir(_dir), "20261120-070000_start.tmp"));
        Directory.CreateDirectory(Path.Combine(SettingsBackup.BackupsDir(_dir), "nije-kopija"));
        Assert.AreEqual(0, SettingsBackup.List(_dir).Count);
        SettingsBackup.Create(_dir, null, "start", T0);
        Assert.AreEqual(1, SettingsBackup.List(_dir).Count);
        Assert.IsFalse(Directory.Exists(Path.Combine(SettingsBackup.BackupsDir(_dir), "20261120-070000_start.tmp")));
    }

    // --- M1b ----------------------------------------------------------------------------------------------------

    private static AppSettings Sample() => new()
    {
        Volume = 35, Theme = "light", Language = "de", NormalizeAudio = true, ResumeMode = "resume", SeekStepSeconds = 10,
        WindowLeft = 100, WindowTop = 50, WindowWidth = 1280, WindowHeight = 720, WindowMaximized = true,
        Equalizer = { Enabled = true, Preset = "rock" },
        SubtitleStyle = { FontSize = 52, Color = "#FFE14D" },
        Online = { AutoSearch = false, OpenSubtitlesApiKey = "enc-key", OpenSubtitlesUsername = "user", OpenSubtitlesPassword = "enc-pass", TitloviEnabled = true },
    };

    [TestMethod]
    public void Export_leaves_out_secrets_and_window_position()
    {
        string json = SettingsTransfer.Export(Sample(), "1.4.0", T0);
        StringAssert.Contains(json, "\"kind\": \"jebaited_player.settings\"");
        StringAssert.Contains(json, "\"Equalizer\"");
        foreach (string secret in new[] { "enc-key", "enc-pass", "\"user\"", "OpenSubtitlesApiKey", "WindowLeft", "WindowMaximized" })
        {
            Assert.IsFalse(json.Contains(secret, StringComparison.Ordinal), secret);
        }
    }

    [TestMethod]
    public void Export_then_import_restores_everything_else()
    {
        AppSettings imported = SettingsTransfer.Read(SettingsTransfer.Export(Sample(), "1.4.0", T0));
        var target = new AppSettings
        {
            WindowLeft = 5, WindowWidth = 800,
            Online = { OpenSubtitlesApiKey = "my-own-key", OpenSubtitlesPassword = "my-own-pass" },
        };
        SettingsTransfer.ApplyTo(target, imported);

        Assert.AreEqual(35, target.Volume);
        Assert.AreEqual("light", target.Theme);
        Assert.AreEqual("de", target.Language);
        Assert.AreEqual("resume", target.ResumeMode);
        Assert.AreEqual(10, target.SeekStepSeconds);
        Assert.IsTrue(target.NormalizeAudio && target.Equalizer.Enabled);
        Assert.AreEqual("rock", target.Equalizer.Preset);
        Assert.AreEqual(52, target.SubtitleStyle.FontSize);
        Assert.IsFalse(target.Online.AutoSearch);
        Assert.IsTrue(target.Online.TitloviEnabled);
        Assert.AreEqual("my-own-key", target.Online.OpenSubtitlesApiKey, "Tvoji ključevi ostaju.");
        Assert.AreEqual("my-own-pass", target.Online.OpenSubtitlesPassword);
        Assert.AreEqual(5, target.WindowLeft, "Položaj prozora ostaje.");
        Assert.AreEqual(800, target.WindowWidth);
    }

    [TestMethod]
    public void A_settings_json_from_a_backup_can_be_imported()
    {
        AppSettings imported = SettingsTransfer.Read(JsonSerializer.Serialize(Sample()));
        Assert.AreEqual("light", imported.Theme);
        Assert.AreEqual("rock", imported.Equalizer.Preset);
    }

    [TestMethod]
    public void Wrong_files_are_rejected_and_change_nothing()
    {
        foreach (string bad in new[] { "", "not json", "[1,2,3]", """{ "foo": 1, "bar": 2 }""", """{ "kind": "jebaited_player.settings" }""",
                     """{ "Volume": "glasno", "Theme": 5, "Language": [] }""" })
        {
            Assert.ThrowsException<InvalidDataException>(() => SettingsTransfer.Read(bad), bad);
        }
    }

    [TestMethod]
    public void Hand_edited_values_in_an_import_are_repaired()
    {
        AppSettings imported = SettingsTransfer.Read("""{ "Volume": 50, "Theme": "dark", "ResumeMode": "maybe", "SeekStepSeconds": 7, "Equalizer": { "Preset": "metal" } }""");
        Assert.AreEqual("ask", imported.ResumeMode);
        Assert.AreEqual(5, imported.SeekStepSeconds);
        Assert.AreEqual("flat", imported.Equalizer.Preset);
    }
}
