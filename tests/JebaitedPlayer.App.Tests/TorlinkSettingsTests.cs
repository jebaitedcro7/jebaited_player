using System.IO;
using System.Text.Json;
using System.Windows.Input;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Views;

namespace JebaitedPlayer.App.Tests;

/// <summary>2.2.14: postavke Torlinka (GitHub inačica): sigurne zadane vrijednosti, spremanje, prečac i redoslijed kartica u Postavkama.</summary>
[TestClass]
public sealed class TorlinkSettingsTests
{
    [TestMethod]
    public void Defaults_AreOffAndSafe()
    {
        var t = new AppSettings().Torlink;
        Assert.IsFalse(t.Enabled, "Torlink je zadano isključen");
        Assert.IsFalse(t.ConsentAccepted, "prihvat upozorenja se traži prije prvog uključivanja");
        Assert.IsFalse(t.SeedAfter, "dijeljenje je zadano isključeno");
        Assert.AreEqual(3, t.MaxDownloads);
        Assert.IsTrue(t.Playlist);
        Assert.AreEqual(string.Empty, t.DownloadDir);
        Assert.AreEqual(0, t.PeerPort);
        Assert.AreEqual(string.Empty, t.LastUpdate);
    }

    [TestMethod]
    public void ToOptions_ClampsAndCleans()
    {
        var t = new TorlinkSettings { MaxDownloads = 99, SeedHours = -5, PeerPort = 70000, Trackers = ["  udp://a:1/announce ", "", "  "] };
        var o = t.ToOptions();
        Assert.AreEqual(20, o.MaxDownloads);
        Assert.AreEqual(0, o.SeedHours);
        Assert.AreEqual(0, o.PeerPort);
        CollectionAssert.AreEqual(new[] { "udp://a:1/announce" }, o.Trackers.ToArray());
        Assert.AreEqual(Path.Combine("Downloads", "torlink"), o.EffectiveDownloadDir[^Path.Combine("Downloads", "torlink").Length..]);
    }

    [TestMethod]
    public void ResetOptions_KeepsEnabledConsentAndHistory()
    {
        var t = new TorlinkSettings
        {
            Enabled = true, ConsentAccepted = true, LastUpdate = "2026-11-22T10:00:00Z", DownloadDir = @"E:\x", MaxDownloads = 7, SeedAfter = true,
            SeedHours = 4, Interface = "wg0", PeerPort = 4000, Trackers = ["udp://a"], Playlist = false,
        };
        t.ResetOptions();
        Assert.IsTrue(t.Enabled);
        Assert.IsTrue(t.ConsentAccepted);
        Assert.AreEqual("2026-11-22T10:00:00Z", t.LastUpdate);
        Assert.AreEqual(string.Empty, t.DownloadDir);
        Assert.AreEqual(3, t.MaxDownloads);
        Assert.IsFalse(t.SeedAfter);
        Assert.AreEqual(0, t.PeerPort);
        Assert.AreEqual(0, t.Trackers.Count);
        Assert.IsTrue(t.Playlist);
    }

    [TestMethod]
    public void Settings_RoundTripThroughJson_AndOldFilesWithoutTorlinkLoadWithDefaults()
    {
        var s = new AppSettings();
        s.Torlink.Enabled = true;
        s.Torlink.DownloadDir = @"E:\Torrents";
        s.Torlink.Trackers = ["udp://t:1"];
        string json = JsonSerializer.Serialize(s);
        var back = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.IsTrue(back.Torlink.Enabled);
        Assert.AreEqual(@"E:\Torrents", back.Torlink.DownloadDir);
        Assert.AreEqual("udp://t:1", back.Torlink.Trackers.Single());

        var old = JsonSerializer.Deserialize<AppSettings>("""{"Language":"hr","Volume":40}""")!;
        Assert.IsNotNull(old.Torlink);
        Assert.IsFalse(old.Torlink.Enabled);
    }

    [TestMethod]
    public void Shortcut_IsCtrlShiftT_NotCtrlT_AndDoesNotClashWithAnyOtherDefault()
    {
        var s = new AppSettings();
        KeyChord[] keys = Shortcuts.Get(s, "torlink");
        Assert.AreEqual(1, keys.Count(k => !k.IsEmpty));
        Assert.AreEqual(new KeyChord(Key.T, ModifierKeys.Control | ModifierKeys.Shift), keys.First(k => !k.IsEmpty));
        Assert.AreEqual("loadSubtitles", Shortcuts.Find(s, new KeyChord(Key.T, ModifierKeys.Control)), "Ctrl+T ostaje za titlove");
        Assert.AreEqual("torlink", Shortcuts.Find(s, new KeyChord(Key.T, ModifierKeys.Control | ModifierKeys.Shift)));
    }

    [TestMethod]
    public void SettingsTab_TorlinkIsLast_SoExistingIndicesDoNotChange()
    {
        Assert.AreEqual(0, (int)SettingsTab.General);
        Assert.AreEqual(8, (int)SettingsTab.Updates, "Obavijesti su umetnute ispred Nadogradnji (2.3)");
        Assert.AreEqual(9, (int)SettingsTab.About);
        Assert.AreEqual(10, (int)SettingsTab.Torlink, "Torlink je i dalje zadnji u enumu; kartica se u prozoru umeće ispred Nadogradnji");
        Assert.IsNull(PlayerSettingsWindow.DefaultsKey(SettingsTab.Torlink), "kartica Torlink ima vlastiti gumb za vraćanje na zadano");
    }
}
