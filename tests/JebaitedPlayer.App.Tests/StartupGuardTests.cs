using System.IO;
using System.Windows;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.App.Tests;

/// <summary>2.1.12: čuvar pokretanja (oznaka, sigurno pokretanje, „prozor na zaslonu”) i odgovor „zauzet” jedne instance.</summary>
[TestClass]
public sealed class StartupGuardTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "jp-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public void Marker_is_written_on_start_and_removed_when_a_window_is_shown()
    {
        string dir = TempDir();
        try
        {
            Assert.IsFalse(StartupGuard.Begin(dir, "2.1.12", DateTime.UtcNow), "first start is normal");
            string marker = Path.Combine(dir, StartupGuard.FileName);
            Assert.IsTrue(File.Exists(marker));
            StringAssert.Contains(File.ReadAllText(marker), "2.1.12");

            Assert.IsTrue(StartupGuard.Complete(dir));
            Assert.IsFalse(File.Exists(marker));
            Assert.IsFalse(StartupGuard.Complete(dir), "second call does nothing");
            Assert.IsFalse(StartupGuard.Begin(dir, "2.1.12", DateTime.UtcNow), "previous start finished normally");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void Leftover_marker_means_safe_start_even_if_it_is_damaged()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, StartupGuard.FileName), "{ oštećeno");
            Assert.IsTrue(StartupGuard.Begin(dir, "2.1.12", DateTime.UtcNow));

            string missing = Path.Combine(dir, "nova", "mapa");
            Assert.IsFalse(StartupGuard.Begin(missing, "2.1.12", DateTime.UtcNow), "missing data folder is created, not an error");
            Assert.IsTrue(File.Exists(Path.Combine(missing, StartupGuard.FileName)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void Safe_start_changes_only_window_placement()
    {
        AppSettings s = AppSettings.Parse("""
            { "WindowMaximized": true, "WindowLeft": -32000, "WindowTop": -32000, "WindowWidth": 1200, "WindowHeight": 700,
              "Volume": 37, "Theme": "light", "MiniPlayer": { "WasOpen": true, "Design": "card" } }
            """);
        StartupGuard.ApplySafeStart(s);
        Assert.IsFalse(s.WindowMaximized);
        Assert.IsNull(s.WindowLeft);
        Assert.IsNull(s.WindowTop);
        Assert.IsFalse(s.MiniPlayer.WasOpen);
        Assert.AreEqual(1200, s.WindowWidth);
        Assert.AreEqual(37, s.Volume);
        Assert.AreEqual("light", s.Theme);
        Assert.AreEqual("card", s.MiniPlayer.Design);
    }

    [TestMethod]
    public void Usable_window_must_be_visible_and_partly_on_a_screen()
    {
        var screen = new Rect(0, 0, 1920, 1080);
        Assert.IsTrue(StartupGuard.IsUsable(new Rect(100, 100, 800, 600), screen, true, false, true, 1));
        Assert.IsTrue(StartupGuard.IsUsable(new Rect(1880, 1040, 800, 600), screen, true, false, true, 1), "40 px still reachable");
        Assert.IsFalse(StartupGuard.IsUsable(new Rect(1900, 100, 800, 600), screen, true, false, true, 1), "only 20 px on screen");
        Assert.IsFalse(StartupGuard.IsUsable(new Rect(-32000, -32000, 800, 600), screen, true, false, false, 1), "hidden start position");
        Assert.IsFalse(StartupGuard.IsUsable(new Rect(100, 100, 800, 600), screen, false, false, true, 1), "hidden window");
        Assert.IsFalse(StartupGuard.IsUsable(new Rect(100, 100, 800, 600), screen, true, false, true, 0), "transparent window");
        Assert.IsTrue(StartupGuard.IsUsable(new Rect(-32000, -32000, 160, 28), screen, true, true, true, 1), "minimized to the taskbar");
        Assert.IsFalse(StartupGuard.IsUsable(new Rect(-32000, -32000, 160, 28), screen, true, true, false, 1), "minimized without a taskbar button");

        // Drugi zaslon lijevo od glavnog (negativne koordinate).
        var two = new Rect(-2560, 0, 4480, 1440);
        Assert.IsTrue(StartupGuard.IsUsable(new Rect(-2000, 200, 800, 600), two, true, false, true, 1));
    }
}

[TestClass]
public sealed class SingleInstanceBusyTests
{
    [TestMethod]
    public void Frozen_primary_answers_busy_and_the_new_launch_runs_on_its_own()
    {
        TimeSpan old = SingleInstance.DeliveryTimeout;
        SingleInstance.DeliveryTimeout = TimeSpan.FromMilliseconds(600);
        try
        {
            string name = "jebaited_test_" + Guid.NewGuid().ToString("N");
            using SingleInstance? primary = SingleInstance.Acquire(name, [], TimeSpan.FromSeconds(5), out _);
            Assert.IsNotNull(primary);

            // UI koji nikad ne preuzme datoteke (zamrznut ili bez prozora); otkazani zahtjev se ne smije otvoriti.
            var never = new TaskCompletionSource();
            CancellationToken seen = default;
            primary.SetHandler((_, ct) =>
            {
                seen = ct;
                return never.Task;
            });

            SingleInstance? second = null;
            bool standalone = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var thread = new Thread(() => second = SingleInstance.Acquire(name, [@"C:\film.mkv"], TimeSpan.FromSeconds(8), out standalone));
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));
            Assert.IsNull(second);
            Assert.IsTrue(standalone, "the new launch must open as its own player");
            Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(4), $"took {sw.Elapsed.TotalSeconds:0.0} s (should not wait the full 8 s)");
            Assert.IsTrue(seen.IsCancellationRequested, "the frozen player must not open the file later");
        }
        finally
        {
            SingleInstance.DeliveryTimeout = old;
        }
    }

    [TestMethod]
    public void Slow_but_working_ui_still_receives_the_files()
    {
        string name = "jebaited_test_" + Guid.NewGuid().ToString("N");
        using SingleInstance? primary = SingleInstance.Acquire(name, [], TimeSpan.FromSeconds(5), out _);
        var received = new List<string>();
        primary!.SetHandler(async (files, ct) =>
        {
            await Task.Delay(300, ct);
            lock (received)
            {
                received.AddRange(files);
            }
        });

        SingleInstance? second = null;
        bool standalone = true;
        var thread = new Thread(() => second = SingleInstance.Acquire(name, [@"C:\pjesma.mp3"], TimeSpan.FromSeconds(8), out standalone));
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.IsNull(second);
        Assert.IsFalse(standalone);
        lock (received)
        {
            CollectionAssert.AreEqual(new[] { @"C:\pjesma.mp3" }, received);
        }
    }
}
