using System.IO;
using System.Collections.Concurrent;
using JebaitedPlayer.Infrastructure;
using Microsoft.Win32;

namespace JebaitedPlayer.App.Tests;

[TestClass]
public sealed class StartupOptionsTests
{
    [TestMethod]
    public void ParsesWindowsIntegrationFlags()
    {
        StartupOptions o = StartupOptions.Parse(["--new-window", @"C:\Video\a.mkv"]);
        Assert.IsTrue(o.NewWindow);
        Assert.AreEqual(@"C:\Video\a.mkv", o.Files.Single());
        Assert.IsNull(o.RegisterCommand);

        Assert.AreEqual("register", StartupOptions.Parse(["--register"]).RegisterCommand);
        Assert.AreEqual("unregister", StartupOptions.Parse(["--unregister"]).RegisterCommand);

        StartupOptions d = StartupOptions.Parse(["--diag-instance-name", "test1", "--diag-registry-root", @"Software\x"]);
        Assert.AreEqual("test1", d.DiagInstanceName);
        Assert.AreEqual(@"Software\x", d.DiagRegistryRoot);
        Assert.AreEqual(0, d.Files.Count);
    }

    [TestMethod]
    public void RelativeFileBecomesAbsolute()
    {
        StartupOptions o = StartupOptions.Parse(["film.mkv"]);
        Assert.IsTrue(Path.IsPathFullyQualified(o.Files.Single()));
    }
}

[TestClass]
public sealed class SingleInstanceTests
{
    private static string UniqueName() => "jebaited_test_" + Guid.NewGuid().ToString("N");

    [TestMethod]
    public void MessageRoundTripKeepsAbsolutePathsOnly()
    {
        string line = SingleInstance.Serialize([@"C:\Video\Š č ć ž đ.mkv", @"\\server\share\b.mp4", "--diag", "relativno.mkv", @"\\.\pipe\x", @"\\?\C:\x.mkv", ""]);
        CollectionAssert.AreEqual(new[] { @"C:\Video\Š č ć ž đ.mkv", @"\\server\share\b.mp4" }, SingleInstance.Deserialize(line).ToArray());
        Assert.AreEqual(0, SingleInstance.Deserialize("nije json").Count);
        Assert.AreEqual(0, SingleInstance.Deserialize("{\"Files\":null}").Count);
    }

    [TestMethod]
    public void ScopeIsPerUserAndSession()
    {
        string scope = SingleInstance.Scope("jebaited_player");
        StringAssert.StartsWith(scope, "jebaited_player-S-1-");
        StringAssert.EndsWith(scope, "-" + System.Diagnostics.Process.GetCurrentProcess().SessionId);
    }

    [TestMethod]
    public void SecondLaunchHandsFilesToFirst()
    {
        string name = UniqueName();
        using SingleInstance? primary = SingleInstance.Acquire(name, [@"C:\a.mkv"], TimeSpan.FromSeconds(5), out bool standalone);
        Assert.IsNotNull(primary);
        Assert.IsFalse(standalone);

        var received = new BlockingCollection<IReadOnlyList<string>>();
        primary.SetHandler(received.Add);

        // Mutex pripada dretvi, pa se "drugo pokretanje" glumi na drugoj dretvi.
        SingleInstance? second = null;
        bool secondStandalone = true;
        var thread = new Thread(() => second = SingleInstance.Acquire(name, [@"C:\b.mkv", @"C:\c.mkv"], TimeSpan.FromSeconds(5), out secondStandalone));
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.IsNull(second, "drugo pokretanje ne smije postati glavno");
        Assert.IsFalse(secondStandalone);

        Assert.IsTrue(received.TryTake(out IReadOnlyList<string>? files, TimeSpan.FromSeconds(5)));
        CollectionAssert.AreEqual(new[] { @"C:\b.mkv", @"C:\c.mkv" }, files!.ToArray());
        Assert.AreEqual(1, primary.ReceivedBatches);
    }

    [TestMethod]
    public void FilesArrivingBeforeHandlerAreKept()
    {
        string name = UniqueName();
        using SingleInstance? primary = SingleInstance.Acquire(name, [], TimeSpan.FromSeconds(5), out _);
        Assert.IsNotNull(primary);
        RunOnThread(() => Assert.IsNull(SingleInstance.Acquire(name, [@"C:\rano.mkv"], TimeSpan.FromSeconds(5), out _)));

        var received = new List<IReadOnlyList<string>>();
        primary.SetHandler(received.Add);
        Assert.AreEqual(1, received.Count);
        Assert.AreEqual(@"C:\rano.mkv", received[0].Single());
    }

    [TestMethod]
    public void EmptyLaunchOnlyActivatesFirst()
    {
        string name = UniqueName();
        using SingleInstance? primary = SingleInstance.Acquire(name, [], TimeSpan.FromSeconds(5), out _);
        var received = new BlockingCollection<IReadOnlyList<string>>();
        primary!.SetHandler(received.Add);
        RunOnThread(() => Assert.IsNull(SingleInstance.Acquire(name, [], TimeSpan.FromSeconds(5), out _)));
        Assert.IsTrue(received.TryTake(out IReadOnlyList<string>? files, TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, files!.Count);
    }

    [TestMethod]
    public void ManySimultaneousLaunchesGiveOnePrimary()
    {
        // Kao Explorer s 8 označenih datoteka: 8 pokretanja u istom trenutku.
        string name = UniqueName();
        var primaries = new ConcurrentBag<SingleInstance>();
        var received = new ConcurrentBag<string>();
        var ownFiles = new ConcurrentBag<string>();
        var standalones = new ConcurrentBag<string>();
        using var start = new ManualResetEventSlim();
        var threads = Enumerable.Range(1, 8).Select(i => new Thread(() =>
        {
            start.Wait();
            string file = $@"C:\Serija\E{i:00}.mkv";
            SingleInstance? instance = SingleInstance.Acquire(name, [file], TimeSpan.FromSeconds(10), out bool standalone);
            if (standalone)
            {
                standalones.Add(file);
            }

            if (instance != null)
            {
                ownFiles.Add(file);
                instance.SetHandler(files =>
                {
                    foreach (string f in files)
                    {
                        received.Add(f);
                    }
                });
                primaries.Add(instance);

                // Glavni ostaje "otvoren" dok ostali ne predaju datoteke.
                SpinWait.SpinUntil(() => received.Count >= 7, TimeSpan.FromSeconds(15));
                instance.Dispose();
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        start.Set();
        Assert.IsTrue(threads.All(t => t.Join(TimeSpan.FromSeconds(30))));

        Assert.AreEqual(0, standalones.Count, "nijedno pokretanje ne smije ostati samostalno");
        Assert.AreEqual(1, primaries.Count, "točno jedan glavni player");
        CollectionAssert.AreEquivalent(
            Enumerable.Range(1, 8).Select(i => $@"C:\Serija\E{i:00}.mkv").ToArray(),
            received.Concat(ownFiles).ToArray());
    }

    [TestMethod]
    public void ReleasedPrimaryLetsNextLaunchStartNormally()
    {
        string name = UniqueName();
        SingleInstance? first = SingleInstance.Acquire(name, [], TimeSpan.FromSeconds(5), out _);
        Assert.IsNotNull(first);
        first.Release();

        SingleInstance? next = null;
        RunOnThread(() =>
        {
            next = SingleInstance.Acquire(name, [@"C:\x.mkv"], TimeSpan.FromSeconds(5), out bool standalone);
            Assert.IsFalse(standalone);
            next?.Dispose();
        });
        Assert.IsNotNull(next, "nakon zatvaranja prvog, novo pokretanje je glavno");
        first.Dispose();
    }

    private static void RunOnThread(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        t.Start();
        Assert.IsTrue(t.Join(TimeSpan.FromSeconds(15)));
        if (error != null)
        {
            throw error;
        }
    }
}

[TestClass]
public sealed class OpenRequestMergerTests
{
    [TestMethod]
    public void MergesRequestsInsideWindow()
    {
        var merger = new OpenRequestMerger(TimeSpan.FromSeconds(2));
        var t0 = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        CollectionAssert.AreEqual(new[] { "a" }, merger.Add(["a"], t0).ToArray());
        CollectionAssert.AreEqual(new[] { "a", "b" }, merger.Add(["b", "A"], t0.AddMilliseconds(300)).ToArray());
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, merger.Add(["c"], t0.AddMilliseconds(2100)).ToArray(), "razmak se mjeri od zadnjeg zahtjeva");
        CollectionAssert.AreEqual(new[] { "d" }, merger.Add(["d"], t0.AddSeconds(10)).ToArray(), "kasnije otvaranje je novi popis");
    }
}

[TestClass]
public sealed class ShellIntegrationTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Init() => _root = @"Software\jebaited_player_test_" + Guid.NewGuid().ToString("N");

    [TestCleanup]
    public void Cleanup() => Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    [TestMethod]
    public void RegisterWritesOpenWithDefaultAppsAndAppPaths()
    {
        const string exe = @"C:\Users\test\AppData\Local\JebaitedPlayer\current\jebaited_player.exe";
        int count = ShellIntegration.Register(exe, _root);
        Assert.AreEqual(MediaFormats.Video.Length + MediaFormats.Audio.Length, count);

        RegistryKey hkcu = Registry.CurrentUser;
        using (RegistryKey? mkv = hkcu.OpenSubKey($@"{_root}\Classes\jebaited_player.mkv\shell\open\command"))
        {
            Assert.AreEqual($"\"{exe}\" \"%1\"", mkv?.GetValue(string.Empty));
        }

        using (RegistryKey? open = hkcu.OpenSubKey($@"{_root}\Classes\jebaited_player.mkv\shell\open"))
        {
            Assert.AreEqual("Player", open?.GetValue("MultiSelectModel"));
        }

        using (RegistryKey? icon = hkcu.OpenSubKey($@"{_root}\Classes\jebaited_player.mp4\DefaultIcon"))
        {
            Assert.AreEqual($"\"{exe}\",0", icon?.GetValue(string.Empty));
        }

        using (RegistryKey? openWith = hkcu.OpenSubKey($@"{_root}\Classes\.mp3\OpenWithProgids"))
        {
            Assert.AreEqual(RegistryValueKind.None, openWith!.GetValueKind("jebaited_player.mp3"));
        }

        using (RegistryKey? assoc = hkcu.OpenSubKey($@"{_root}\jebaited_player\Capabilities\FileAssociations"))
        {
            Assert.AreEqual("jebaited_player.mkv", assoc?.GetValue(".mkv"));
            Assert.IsNull(assoc?.GetValue(".mp3"), "glazba nije u Zadanim aplikacijama (\"Postavi zadano\" ne preuzima glazbu)");
        }

        using (RegistryKey? apps = hkcu.OpenSubKey($@"{_root}\RegisteredApplications"))
        {
            Assert.AreEqual($@"{_root}\jebaited_player\Capabilities", apps?.GetValue("jebaited_player"));
        }

        using (RegistryKey? app = hkcu.OpenSubKey($@"{_root}\Classes\Applications\jebaited_player.exe"))
        {
            Assert.AreEqual("jebaited_player", app?.GetValue("FriendlyAppName"));
        }

        using (RegistryKey? appPath = hkcu.OpenSubKey($@"{_root}\Microsoft\Windows\CurrentVersion\App Paths\jebaited_player.exe"))
        {
            Assert.AreEqual(exe, appPath?.GetValue(string.Empty));
        }

        Assert.IsTrue(ShellIntegration.IsRegisteredFor(exe, _root));
        Assert.IsFalse(ShellIntegration.IsRegisteredFor(@"D:\drugi\jebaited_player.exe", _root));
    }

    [TestMethod]
    public void UnregisterRemovesOnlyOwnEntries()
    {
        RegistryKey hkcu = Registry.CurrentUser;

        // Tuđa registracija za isti format mora ostati.
        using (RegistryKey other = hkcu.CreateSubKey($@"{_root}\Classes\.mkv\OpenWithProgids"))
        {
            other.SetValue("VLC.mkv", Array.Empty<byte>(), RegistryValueKind.None);
        }

        using (RegistryKey apps = hkcu.CreateSubKey($@"{_root}\RegisteredApplications"))
        {
            apps.SetValue("VLC", @"Software\Clients\Media\VLC\Capabilities");
        }

        ShellIntegration.Register(@"C:\p\jebaited_player.exe", _root);
        ShellIntegration.Register(@"C:\p\jebaited_player.exe", _root); // ponavljanje je sigurno
        ShellIntegration.Unregister(_root);

        Assert.IsNull(ShellIntegration.RegisteredExe(_root));
        using (RegistryKey? classes = hkcu.OpenSubKey($@"{_root}\Classes"))
        {
            Assert.IsFalse(classes!.GetSubKeyNames().Any(n => n.StartsWith("jebaited_player.", StringComparison.OrdinalIgnoreCase)));
            Assert.IsNull(classes.OpenSubKey(@"Applications\jebaited_player.exe"));
        }

        using (RegistryKey? openWith = hkcu.OpenSubKey($@"{_root}\Classes\.mkv\OpenWithProgids"))
        {
            CollectionAssert.AreEqual(new[] { "VLC.mkv" }, openWith!.GetValueNames());
        }

        using (RegistryKey? apps = hkcu.OpenSubKey($@"{_root}\RegisteredApplications"))
        {
            CollectionAssert.AreEqual(new[] { "VLC" }, apps!.GetValueNames());
        }

        Assert.IsNull(hkcu.OpenSubKey($@"{_root}\jebaited_player"));
        Assert.IsNull(hkcu.OpenSubKey($@"{_root}\Microsoft\Windows\CurrentVersion\App Paths\jebaited_player.exe"));
    }

    [TestMethod]
    public void ReRegisterMovesToNewExe()
    {
        ShellIntegration.Register(@"C:\stari\jebaited_player.exe", _root);
        ShellIntegration.Register(@"C:\novi\jebaited_player.exe", _root);
        Assert.IsTrue(ShellIntegration.IsRegisteredFor(@"C:\novi\jebaited_player.exe", _root));
        using RegistryKey? cmd = Registry.CurrentUser.OpenSubKey($@"{_root}\Classes\jebaited_player.avi\shell\open\command");
        StringAssert.Contains((string)cmd!.GetValue(string.Empty)!, @"C:\novi\");
    }
}
