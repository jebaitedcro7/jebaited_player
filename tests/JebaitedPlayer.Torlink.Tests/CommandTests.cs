using System.Text.Json;

namespace JebaitedPlayer.Torlink.Tests;

[TestClass]
public class CommandTests
{
    private static readonly TorlinkPaths Paths = new(@"C:\data");

    [TestMethod]
    public void ServeArgs_NeverContainToken_AndBindLoopback()
    {
        IReadOnlyList<string> args = TorlinkCommand.ServeArgs(Paths, new TorlinkOptions(), 19161);
        IReadOnlyDictionary<string, string> env = TorlinkCommand.Environment(Paths, new TorlinkOptions(), "SECRETTOKEN");
        Assert.IsFalse(args.Any(a => a.Contains("SECRETTOKEN")), "token ne smije u argumente (vidljivi su drugim procesima)");
        Assert.AreEqual("SECRETTOKEN", env[TorlinkCommand.TokenVariable]);
        CollectionAssert.Contains(args.ToList(), "127.0.0.1");
        Assert.AreEqual("serve", args[1]);
    }

    [TestMethod]
    public void Environment_IsolatesStateAndDisablesUpdateCheckAndWebRtc()
    {
        IReadOnlyDictionary<string, string> env = TorlinkCommand.Environment(Paths, new TorlinkOptions { MaxDownloads = 5 }, null);
        Assert.AreEqual(Paths.StateDir, env["TORLINK_STATE_DIR"]);
        Assert.AreEqual("1", env["TORLINK_NO_UPDATE_CHECK"]);
        Assert.AreEqual("1", env["TORLINK_NO_WEBRTC"]);
        Assert.AreEqual("5", env["TORLINK_MAX_DOWNLOADS"]);
        Assert.IsFalse(env.ContainsKey(TorlinkCommand.TokenVariable));
    }

    [TestMethod]
    public void SeedTime_IsOneSecondWhenSeedingIsOff_AndHoursWhenOn()
    {
        IReadOnlyList<string> off = TorlinkCommand.ServeArgs(Paths, new TorlinkOptions { SeedAfter = false }, 1);
        Assert.AreEqual("1", off[off.ToList().IndexOf("--seed-time") + 1]);
        IReadOnlyList<string> on = TorlinkCommand.ServeArgs(Paths, new TorlinkOptions { SeedAfter = true, SeedHours = 2 }, 1);
        Assert.AreEqual("7200", on[on.ToList().IndexOf("--seed-time") + 1]);
        IReadOnlyList<string> forever = TorlinkCommand.ServeArgs(Paths, new TorlinkOptions { SeedAfter = true, SeedHours = 0 }, 1);
        Assert.AreEqual("0", forever[forever.ToList().IndexOf("--seed-time") + 1]);
    }

    [TestMethod]
    public void ServeArgs_PlaylistAndNetworkFlags()
    {
        IReadOnlyList<string> a = TorlinkCommand.ServeArgs(Paths, new TorlinkOptions { Playlist = false, Interface = " wg0 ", PeerPort = 51413 }, 9);
        CollectionAssert.Contains(a.ToList(), "--no-playlist");
        Assert.AreEqual("wg0", a[a.ToList().IndexOf("--interface") + 1]);
        Assert.AreEqual("51413", a[a.ToList().IndexOf("--peer-port") + 1]);
        IReadOnlyList<string> b = TorlinkCommand.ServeArgs(Paths, new TorlinkOptions { PeerPort = 70000 }, 9);
        CollectionAssert.DoesNotContain(b.ToList(), "--peer-port");
        CollectionAssert.DoesNotContain(b.ToList(), "--no-playlist");
    }

    [TestMethod]
    public void SearchArgs_QueryIsSingleArgument()
    {
        IReadOnlyList<string> a = TorlinkCommand.SearchArgs(Paths, new TorlinkOptions(), "big buck bunny", "movies", 20);
        Assert.AreEqual("big buck bunny", a[2]);
        Assert.AreEqual("movies", a[a.ToList().IndexOf("--category") + 1]);
        Assert.AreEqual("20s", a[a.ToList().IndexOf("--timeout") + 1]);
        Assert.IsFalse(TorlinkCommand.SearchArgs(Paths, new TorlinkOptions(), "x", null, 20).Contains("--category"));
    }

    [TestMethod]
    public void NormalizeQuery_StripsControlCharsDashesAndLimitsLength()
    {
        Assert.AreEqual("help", SearchRunner.NormalizeQuery("--help"));
        Assert.AreEqual("a b", SearchRunner.NormalizeQuery("  a\tb\n "));
        Assert.IsNull(SearchRunner.NormalizeQuery("  "));
        Assert.IsNull(SearchRunner.NormalizeQuery("---"));
        Assert.IsNull(SearchRunner.NormalizeQuery(null));
        Assert.AreEqual(SearchRunner.MaxQueryLength, SearchRunner.NormalizeQuery(new string('a', 500))!.Length);
    }

    [TestMethod]
    public void ConfigJson_HasOnlyWhatTorlinkReads()
    {
        string json = TorlinkCommand.ConfigJson(new TorlinkOptions { DownloadDir = @"E:\Torrents", Trackers = ["udp://t:1/announce", "  ", "http://x/a"] });
        using JsonDocument d = JsonDocument.Parse(json);
        Assert.AreEqual(@"E:\Torrents", d.RootElement.GetProperty("downloadDir").GetString());
        Assert.AreEqual(2, d.RootElement.GetProperty("trackers").GetArrayLength());
        Assert.AreEqual(2, d.RootElement.EnumerateObject().Count());
    }

    [TestMethod]
    public void WriteConfig_CreatesFileAtomically()
    {
        using var t = new TempDir();
        TorlinkCommand.WriteConfig(t.Paths, new TorlinkOptions { DownloadDir = @"E:\x" });
        Assert.IsTrue(File.Exists(t.Paths.ConfigFile));
        Assert.IsFalse(File.Exists(t.Paths.ConfigFile + ".tmp"));
    }

    [TestMethod]
    public void Defaults_AreSafe()
    {
        var o = new TorlinkOptions();
        Assert.IsFalse(o.SeedAfter, "dijeljenje je zadano isključeno");
        Assert.AreEqual(3, o.MaxDownloads);
        Assert.IsTrue(o.EffectiveDownloadDir.EndsWith(Path.Combine("Downloads", "torlink")));
        Assert.AreEqual(@"D:\x", new TorlinkOptions { DownloadDir = @" D:\x " }.EffectiveDownloadDir);
    }

    [TestMethod]
    public void Paths_AreUnderTorlinkRoot()
    {
        Assert.AreEqual(@"C:\data\torlink", Paths.Root);
        Assert.IsTrue(Paths.QueueFile.StartsWith(Paths.StateDir));
        Assert.IsTrue(Paths.NodeExe.StartsWith(Paths.RuntimeDir));
        Assert.AreNotEqual(Paths.RuntimeDir, Paths.PreviousDir);
    }

    [TestMethod]
    public void Version_ParsingAndOrdering()
    {
        Assert.IsTrue(TorlinkVersion.TryParse("v1.11.1", 1, out TorlinkVersion a));
        Assert.IsTrue(TorlinkVersion.TryParse("1.12.0-rc.1", 1, out TorlinkVersion b));
        Assert.IsTrue(b > a);
        Assert.IsTrue(new TorlinkVersion(1, 11, 1, 2) > a, "viša revizija našeg paketa je novija");
        Assert.IsFalse(TorlinkVersion.TryParse("abc", 1, out _));
        Assert.IsFalse(TorlinkVersion.TryParse("", 1, out _));
        Assert.IsFalse(TorlinkVersion.TryParse("1.2.3.4", 1, out _));
    }

    [TestMethod]
    public void Input_Classification()
    {
        Assert.AreEqual(InputKind.Empty, TorlinkInput.Classify("  "));
        Assert.AreEqual(InputKind.Magnet, TorlinkInput.Classify("magnet:?xt=urn:btih:08ada5a7a6183aae1e09d831df6748d566095a10&dn=Sintel"));
        Assert.AreEqual(InputKind.InfoHash, TorlinkInput.Classify("08ADA5A7A6183AAE1E09D831DF6748D566095A10"));
        Assert.AreEqual(InputKind.Text, TorlinkInput.Classify("big buck bunny"));
        Assert.AreEqual(InputKind.Text, TorlinkInput.Classify("magnet:?dn=nohash"));
        Assert.AreEqual(InputKind.TorrentFile, TorlinkInput.Classify("\"C:\\x\\a.torrent\"", _ => true));
        Assert.AreEqual(InputKind.Text, TorlinkInput.Classify("C:\\x\\a.torrent", _ => false));
        Assert.AreEqual("08ada5a7a6183aae1e09d831df6748d566095a10", TorlinkInput.InfoHashOf("magnet:?xt=urn:btih:08ADA5A7A6183AAE1E09D831DF6748D566095A10"));
        Assert.IsNull(TorlinkInput.InfoHashOf("text"));
    }

    [TestMethod]
    public void DiskGuard_BlocksWhenNotEnoughSpaceAndNeverBlocksWhenUnknown()
    {
        Assert.IsFalse(DiskGuard.Check(@"D:\dl", 3_000_000_000, _ => 1_000_000_000).Enough);
        Assert.IsTrue(DiskGuard.Check(@"D:\dl", 1_000_000_000, _ => 5_000_000_000).Enough);
        Assert.IsTrue(DiskGuard.Check(@"D:\dl", 1_000_000_000, _ => null).Enough, "nepoznat prostor ne blokira");
        Assert.IsTrue(DiskGuard.Check(@"D:\dl", 0, _ => 0).Enough, "nepoznata veličina ne blokira");
        Assert.IsFalse(DiskGuard.Check(@"D:\dl", 900_000_000, _ => 1_000_000_000).Enough, "rezerva od 200 MB");
    }
}
