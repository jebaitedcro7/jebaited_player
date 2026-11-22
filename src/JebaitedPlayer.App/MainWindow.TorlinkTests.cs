#if TORLINK
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;
using JebaitedPlayer.Torlink;
using JebaitedPlayer.Views;
using JebaitedPlayer.Views.Torlink;
using static JebaitedPlayer.Interop.NativeMethods;

namespace JebaitedPlayer;

/// <summary>
/// Stvarni test Torlinka u pravom playeru (<c>--diag-ui-test torlink</c>, samo uz JEBAITED_DATA_DIR): uključivanje s prihvatom upozorenja i
/// instalacijom runtimea (zip preko lokalnog "GitHuba"), pravi klik na "Pretraži online", pretraga, preuzimanje legalnog filma
/// (Blender Foundation) s pauzom i nastavkom, povratak na Home, čip, isključivanje tijekom preuzimanja, pad procesa, ažuriranje
/// (uspješno i neuspješno s automatskim vraćanjem). Ulazi: JEBAITED_TORLINK_ZIP_R1/R2/R3 (zipovi), JEBAITED_TORLINK_API (lokalni poslužitelj).
/// </summary>
public partial class MainWindow
{
    private const string SintelHash = "08ada5a7a6183aae1e09d831df6748d566095a10";
    private const string BigBuckBunnyHash = "dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c";

    private sealed class FakeGitHub : IDisposable
    {
        private readonly HttpListener _l = new();
        private readonly string _base;
        private readonly CancellationTokenSource _cts = new();

        public FakeGitHub(string baseUrl)
        {
            _base = baseUrl.TrimEnd('/');
            _l.Prefixes.Add(_base + "/");
            _l.Start();
            _ = Task.Run(Loop);
        }

        public List<(string Tag, string ZipPath)> Releases { get; } = [];
        public string UpstreamVersion { get; set; } = "1.11.1";
        public bool Down { get; set; }

        private async Task Loop()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext c;
                try
                {
                    c = await _l.GetContextAsync();
                }
                catch (Exception)
                {
                    return;
                }

                try
                {
                    Handle(c);
                }
                catch (Exception)
                {
                    c.Response.StatusCode = 500;
                    c.Response.Close();
                }
            }
        }

        private void Handle(HttpListenerContext c)
        {
            string path = c.Request.Url!.AbsolutePath;
            if (Down)
            {
                c.Response.StatusCode = 503;
                c.Response.Close();
                return;
            }

            byte[] body;
            string type = "application/json";
            if (path == "/upstream")
            {
                body = Encoding.UTF8.GetBytes($$"""{"tag_name":"v{{UpstreamVersion}}","body":"Test notes for {{UpstreamVersion}}","html_url":"https://github.com/baairon/torlink","published_at":"2026-11-16T22:48:58Z"}""");
            }
            else if (path == "/runtime")
            {
                var items = Releases.Select(r =>
                {
                    string name = Path.GetFileName(r.ZipPath);
                    return new
                    {
                        tag_name = r.Tag,
                        body = "test runtime",
                        assets = new object[]
                        {
                            new { name, browser_download_url = $"{_base}/files/{name}", size = new FileInfo(r.ZipPath).Length },
                            new { name = "SHA256SUMS.txt", browser_download_url = $"{_base}/sums/{r.Tag}", size = 100 },
                        },
                    };
                });
                body = JsonSerializer.SerializeToUtf8Bytes(items);
            }
            else if (path.StartsWith("/files/"))
            {
                string name = path["/files/".Length..];
                (string tag, string zip) = Releases.First(r => Path.GetFileName(r.ZipPath) == name);
                body = File.ReadAllBytes(zip);
                type = "application/zip";
            }
            else if (path.StartsWith("/sums/"))
            {
                string tag = path["/sums/".Length..];
                string zip = Releases.First(r => r.Tag == tag).ZipPath;
                body = Encoding.UTF8.GetBytes($"{RuntimeStore.Sha256File(zip)}  {Path.GetFileName(zip)}\n");
                type = "text/plain";
            }
            else
            {
                c.Response.StatusCode = 404;
                c.Response.Close();
                return;
            }

            c.Response.ContentType = type;
            c.Response.ContentLength64 = body.Length;
            c.Response.OutputStream.Write(body);
            c.Response.Close();
        }

        public void Dispose()
        {
            _cts.Cancel();
            _l.Close();
        }
    }

    private async Task ClickElementAsync(FrameworkElement el)
    {
        Point p = el.PointToScreen(new Point(el.ActualWidth / 2, el.ActualHeight / 2));
        SetCursorPos((int)Math.Round(p.X), (int)Math.Round(p.Y));
        await Task.Delay(250);
        MouseButton(true);
        await Task.Delay(80);
        MouseButton(false);
        await Task.Delay(300);
    }

    private string ChipRect()
    {
        if (_torlinkChip is null || !_torlinkChip.IsVisible)
        {
            return "nevidljiv";
        }

        Point p = _torlinkChip.TransformToAncestor(this).Transform(new Point(0, 0));
        return $"{p.X:0},{p.Y:0} {_torlinkChip.ActualWidth:0}x{_torlinkChip.ActualHeight:0} u {ActualWidth:0}x{ActualHeight:0}";
    }

    private static double MbOf(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return Math.Round(p.WorkingSet64 / 1048576.0, 1);
        }
        catch (ArgumentException)
        {
            return -1;
        }
    }

    private static bool Alive(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task<Dictionary<string, object?>> TorlinkUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        try
        {
            await TorlinkUiTestCoreAsync(r);
        }
        catch (Exception ex)
        {
            // Dosadašnji rezultati ostaju (skripta ih provjerava), a greška se vidi u scenarioFailure.
            r["scenarioFailure"] = ex.ToString();
            if (_torlink is not null)
            {
                r["failureState"] = new Dictionary<string, object?>
                {
                    ["status"] = _torlink.Host.Status.ToString(), ["lastError"] = _torlink.Host.Supervisor.LastError, ["installed"] = _torlink.Host.Installed?.Display,
                    ["output"] = _torlink.Host.Supervisor.RecentOutput.TakeLast(8).ToArray(),
                };
            }
        }

        return r;
    }

    private async Task TorlinkUiTestCoreAsync(Dictionary<string, object?> r)
    {
        async Task Within(Task t, int seconds, string name, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            if (await Task.WhenAny(t, Task.Delay(seconds * 1000)) != t)
            {
                throw new TimeoutException($"zapelo: {name} (redak {line})");
            }

            await t;
        }

        string? api = Environment.GetEnvironmentVariable("JEBAITED_TORLINK_API");
        string? zip1 = Environment.GetEnvironmentVariable("JEBAITED_TORLINK_ZIP_R1");
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 } || api is not { Length: > 0 } || zip1 is not { Length: > 0 } || _torlink is null)
        {
            r["error"] = "JEBAITED_DATA_DIR, JEBAITED_TORLINK_API i JEBAITED_TORLINK_ZIP_R1 su obavezni";
            return;
        }

        string? zip2 = Environment.GetEnvironmentVariable("JEBAITED_TORLINK_ZIP_R2");
        string? zip3 = Environment.GetEnvironmentVariable("JEBAITED_TORLINK_ZIP_R3");
        using var gh = new FakeGitHub(api);
        gh.Releases.Add(("torlink-runtime-1.11.1-r1", zip1));
        TorlinkHost host = _torlink.Host;
        TorlinkSettings ts = _settings.Torlink;
        _settings.MiniPlayer.AutoSwitch = false;

        // ---- 0) početno stanje: isključeno = nema gumba, procesa ni runtimea ----
        if (_vm.HasMedia)
        {
            GoHome();
        }

        await WaitForAsync(() => WelcomePanel.IsVisible, 8000);
        r["initial"] = new Dictionary<string, object?>
        {
            ["enabled"] = ts.Enabled, ["status"] = host.Status.ToString(), ["homeButtonVisible"] = _torlinkHomeButton!.IsVisible,
            ["installed"] = host.Installed is not null, ["processId"] = host.Supervisor.ProcessId,
            ["chipVisible"] = _torlinkChip!.IsVisible, ["bodyShortcut"] = Shortcuts.Get(_settings, "torlink").Select(k => k.Serialize()).ToArray(),
        };
        r["initialHomeShot"] = SaveWindowPng("torlink_home_off");

        // ---- 1) Postavke → Torlink: odbijeno upozorenje ne uključuje ništa; prihvaćeno instalira i uključuje ----
        ShowPlayerSettings(SettingsTab.Torlink);
        await WaitForAsync(() => _playerSettingsWindow is { IsLoaded: true }, 5000);
        PlayerSettingsWindow sw = _playerSettingsWindow!;
        sw.AskUnsavedOverride = () => UnsavedChoice.Save;
        TorlinkSettingsPanel panel = sw.TorlinkPanelForTests!;
        r["tabIsTorlink"] = sw.SelectedTab == SettingsTab.Torlink;
        r["settingsOffShot"] = SaveWindowPng("torlink_settings_off", sw);

        TorlinkConsentWindow.AskOverride = () => false;
        await Within(panel.ClickEnableForTestsAsync(), 300, "panel.ClickEnableForTestsAsync");
        r["afterDecline"] = new Dictionary<string, object?> { ["enabled"] = ts.Enabled, ["consent"] = ts.ConsentAccepted, ["installed"] = host.Installed is not null };

        TorlinkConsentWindow.AskOverride = () => true;
        var sw0 = Stopwatch.StartNew();
        await Within(panel.ClickEnableForTestsAsync(), 300, "panel.ClickEnableForTestsAsync");
        r["afterAccept"] = new Dictionary<string, object?>
        {
            ["enabled"] = ts.Enabled, ["consent"] = ts.ConsentAccepted, ["installed"] = host.Installed?.Display, ["seconds"] = Math.Round(sw0.Elapsed.TotalSeconds, 1),
            ["message"] = panel.EnableMessageForTests, ["switchOn"] = panel.EnabledForTests, ["bodyEnabled"] = panel.BodyEnabledForTests,
            ["state"] = panel.StateTextForTests, ["savedInFile"] = File.Exists(AppPaths.SettingsFile) && File.ReadAllText(AppPaths.SettingsFile).Contains("\"Enabled\": true"),
            ["noProcessYet"] = host.Supervisor.ProcessId is null,
        };
        r["settingsOnShot"] = SaveWindowPng("torlink_settings_on", sw);

        // 1b) nova provjera ažuriranja kad nema novijeg
        await Within(panel.ClickCheckForTestsAsync(), 90, "panel.ClickCheckForTestsAsync");
        r["checkNoUpdate"] = new Dictionary<string, object?> { ["text"] = panel.UpdateStatusForTests, ["updateButton"] = panel.UpdateButtonVisibleForTests };
        sw.Close();
        await WaitForAsync(() => _playerSettingsWindow is null, 5000);
        await Task.Delay(300);
        r["homeButtonAfterEnable"] = _torlinkHomeButton.IsVisible;
        r["homeShotOn"] = SaveWindowPng("torlink_home_on");

        // ---- 2) pravi klik na "Pretraži online" ----
        await ClickElementAsync(_torlinkHomeButton);
        await WaitForAsync(() => _torlinkView && _torlinkPage is { IsVisible: true }, 5000);
        TorlinkPage page = _torlinkPage!;
        await WaitForAsync(() => host.Status == TorlinkStatus.Running, 20000);
        r["pageOpen"] = new Dictionary<string, object?>
        {
            ["view"] = _torlinkView, ["pageVisible"] = page.IsVisible, ["welcomeVisible"] = WelcomePanel.IsVisible, ["status"] = host.Status.ToString(),
            ["pid"] = host.Supervisor.ProcessId, ["nodeMb"] = MbOf(host.Supervisor.ProcessId ?? 0), ["banner"] = page.BannerForTests,
        };

        // ---- 3) pretraga ----
        page.SearchTextForTests = "Sintel";
        await Within(page.SearchForTestsAsync(), 90, "page.SearchForTestsAsync");
        r["search"] = new Dictionary<string, object?>
        {
            ["results"] = page.ResultCountForTests, ["chips"] = page.SourceChipTextsForTests, ["message"] = page.MessageForTests,
            ["sources"] = host.LastSources.Select(s => $"{s.Id}:{s.Ok}").ToArray(),
        };
        r["pageSearchShot"] = SaveWindowPng("torlink_page_search");

        // ---- 4) preuzimanje: Sintel (129 MB), pauza, nastavak, završetak ----
        page.SearchTextForTests = SintelHash;
        await Within(page.SearchForTestsAsync(), 90, "page.SearchForTestsAsync");
        await WaitForAsync(() => page.RowsForTests.Any(x => x.BarValue > 0), 60000);
        TorlinkPage.DownloadRow? row = page.RowsForTests.FirstOrDefault();
        await Task.Delay(2500);
        await Within(page.RefreshForTestsAsync(), 90, "page.RefreshForTestsAsync");
        row = page.RowsForTests.FirstOrDefault();
        r["downloading"] = new Dictionary<string, object?>
        {
            ["rows"] = page.DownloadRowCountForTests, ["name"] = row?.NameText, ["status"] = row?.StatusText, ["line"] = row?.LineText, ["bar"] = row?.BarValue,
            ["pauseButton"] = row?.IsVisibleButton("pause"), ["cancelButton"] = row?.IsVisibleButton("delete"), ["summary"] = page.SummaryForTests,
            ["message"] = page.MessageForTests, ["snapshotActive"] = host.Snapshot.Active.Count, ["speed"] = host.Snapshot.TotalSpeed,
        };
        r["pageDownloadShot"] = SaveWindowPng("torlink_page_downloading");

        row?.ClickForTests("pause");
        await WaitForAsync(() => host.Snapshot.Active.Any(i => i.State == DownloadState.Paused), 15000);
        await Within(page.RefreshForTestsAsync(), 90, "page.RefreshForTestsAsync");
        double pausedPct = host.Snapshot.Active.FirstOrDefault()?.Percent ?? -1;
        await Task.Delay(3500);
        await Within(page.RefreshForTestsAsync(), 90, "page.RefreshForTestsAsync");
        r["paused"] = new Dictionary<string, object?>
        {
            ["state"] = host.Snapshot.Active.FirstOrDefault()?.State.ToString(), ["percent"] = pausedPct, ["percentAfter3s"] = host.Snapshot.Active.FirstOrDefault()?.Percent,
            ["status"] = page.RowsForTests.FirstOrDefault()?.StatusText, ["resumeButton"] = page.RowsForTests.FirstOrDefault()?.IsVisibleButton("resume"),
        };

        page.RowsForTests.First().ClickForTests("resume");
        await WaitForAsync(() => host.Snapshot.Active.Any(i => i.State == DownloadState.Downloading), 15000);
        r["resumed"] = host.Snapshot.Active.Any(i => i.State == DownloadState.Downloading);

        // preuzimanje se NE prekida povratkom na Home: čip i nastavak
        await ClickElementAsync(page.HomeButtonForTests);
        await WaitForAsync(() => !_torlinkView && WelcomePanel.IsVisible, 5000);
        await Task.Delay(2500);
        double pctAtHome = host.Snapshot.Active.FirstOrDefault()?.Percent ?? 100;
        r["backHome"] = new Dictionary<string, object?>
        {
            ["view"] = _torlinkView, ["pageVisible"] = page.IsVisible, ["welcomeVisible"] = WelcomePanel.IsVisible, ["chipVisible"] = _torlinkChip.IsVisible,
            ["chipText"] = _torlinkChip.IsVisible ? _torlinkChip.Content as string : null, ["processAlive"] = Alive(host.Supervisor.ProcessId ?? 0),
            ["homeButtonStillVisible"] = _torlinkHomeButton.IsVisible,
            ["chipRect"] = ChipRect(),
        };
        r["homeWithChipShot"] = SaveWindowPng("torlink_home_chip");
        await WaitForAsync(() => (host.Snapshot.Active.FirstOrDefault()?.Percent ?? 100) > pctAtHome || host.Snapshot.Completed.Any(i => i.Id == SintelHash), 90000);
        r["continuesAtHome"] = (host.Snapshot.Active.FirstOrDefault()?.Percent ?? 100) > pctAtHome || host.Snapshot.Completed.Any(i => i.Id == SintelHash);

        // čip otvara stranicu na kartici preuzimanja
        if (_torlinkChip.IsVisible)
        {
            await ClickElementAsync(_torlinkChip);
            await WaitForAsync(() => _torlinkView, 3000);
            r["chipOpensPage"] = _torlinkView;
        }
        else
        {
            r["chipOpensPage"] = "chip not visible (download already finished)";
            OpenTorlink();
        }

        await WaitForAsync(() => host.Snapshot.Completed.Any(i => i.Id == SintelHash), 240000);
        await Task.Delay(3500);
        await Within(page.RefreshForTestsAsync(), 90, "page.RefreshForTestsAsync");
        page.SelectTabForTests("completed");
        await Task.Delay(300);
        DownloadItem? done = host.Snapshot.Completed.FirstOrDefault(i => i.Id == SintelHash);
        TorlinkPage.DownloadRow? crow = page.RowsForTests.FirstOrDefault();
        r["completed"] = new Dictionary<string, object?>
        {
            ["found"] = done is not null, ["seeding"] = done?.Seeding, ["line"] = crow?.LineText, ["playButton"] = crow?.IsVisibleButton("play"), ["folderButton"] = crow?.IsVisibleButton("folder"),
            ["removeButton"] = crow?.IsVisibleButton("remove"), ["dirExists"] = done is not null && Directory.Exists(done.Dir), ["files"] = done is not null && Directory.Exists(done.Dir) ? Directory.EnumerateFiles(done.Dir, "*", SearchOption.AllDirectories).Count() : 0,
        };
        r["pageCompletedShot"] = SaveWindowPng("torlink_page_completed");

        // duplikat završenog
        page.SearchTextForTests = SintelHash;
        await Within(page.SearchForTestsAsync(), 90, "page.SearchForTestsAsync");
        r["duplicateMessage"] = page.MessageForTests;

        // ---- 4b) nema interneta: pretraga ide kroz nedostupan proxy (Node 22.21+ čita HTTPS_PROXY uz NODE_USE_ENV_PROXY) ----
        Environment.SetEnvironmentVariable("NODE_USE_ENV_PROXY", "1");
        Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://127.0.0.1:9");
        Environment.SetEnvironmentVariable("HTTP_PROXY", "http://127.0.0.1:9");
        page.SearchTextForTests = "Big Buck Bunny";
        var sw1 = Stopwatch.StartNew();
        await Within(page.SearchForTestsAsync(), 90, "page.SearchForTestsAsync");
        r["offlineSearch"] = new Dictionary<string, object?>
        {
            ["seconds"] = Math.Round(sw1.Elapsed.TotalSeconds, 1), ["message"] = page.MessageForTests, ["chips"] = page.SourceChipTextsForTests, ["results"] = page.ResultCountForTests,
            ["pageStillResponsive"] = page.IsVisible, ["downloadsListStillThere"] = page.DownloadRowCountForTests >= 0,
        };
        r["offlineShot"] = SaveWindowPng("torlink_page_offline");
        Environment.SetEnvironmentVariable("NODE_USE_ENV_PROXY", null);
        Environment.SetEnvironmentVariable("HTTPS_PROXY", null);
        Environment.SetEnvironmentVariable("HTTP_PROXY", null);

        // ---- 5) Esc vraća na Home (tipkovnica) ----
        var esc = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(page)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        page.RaiseEvent(esc);
        await Task.Delay(300);
        r["escBack"] = !_torlinkView && WelcomePanel.IsVisible;

        // ---- 6) drugo preuzimanje, isključivanje tijekom preuzimanja, nastavak nakon uključivanja ----
        OpenTorlink();
        page.SearchTextForTests = BigBuckBunnyHash;
        await Within(page.SearchForTestsAsync(), 90, "page.SearchForTestsAsync");
        await WaitForAsync(() => host.Snapshot.Active.Any(i => i.Percent >= 3), 90000);
        int pid = host.Supervisor.ProcessId ?? 0;
        r["secondDownloadPercent"] = host.Snapshot.Active.FirstOrDefault()?.Percent;
        CloseTorlink();
        ShowPlayerSettings(SettingsTab.Torlink);
        await WaitForAsync(() => _playerSettingsWindow is { IsLoaded: true }, 5000);
        sw = _playerSettingsWindow!;
        sw.AskUnsavedOverride = () => UnsavedChoice.Save;
        panel = sw.TorlinkPanelForTests!;
        TorlinkSettingsPanel.ConfirmDisableOverride = () => true;
        await Within(panel.ClickEnableForTestsAsync(), 300, "panel.ClickEnableForTestsAsync");
        await Task.Delay(800);
        r["disabledDuring"] = new Dictionary<string, object?>
        {
            ["enabled"] = ts.Enabled, ["status"] = host.Status.ToString(), ["processGone"] = !Alive(pid), ["queueKept"] = File.Exists(new TorlinkPaths(AppPaths.DataDir).QueueFile),
            ["bodyEnabled"] = panel.BodyEnabledForTests, ["settingsKept"] = ts.ConsentAccepted && host.Installed is not null,
        };
        sw.Close();
        await WaitForAsync(() => _playerSettingsWindow is null, 5000);
        r["homeButtonHiddenWhenOff"] = !_torlinkHomeButton.IsVisible;
        r["chipHiddenWhenOff"] = !_torlinkChip.IsVisible;

        ShowPlayerSettings(SettingsTab.Torlink);
        await WaitForAsync(() => _playerSettingsWindow is { IsLoaded: true }, 5000);
        sw = _playerSettingsWindow!;
        sw.AskUnsavedOverride = () => UnsavedChoice.Save;
        panel = sw.TorlinkPanelForTests!;
        await Within(panel.ClickEnableForTestsAsync(), 300, "panel.ClickEnableForTestsAsync");
        r["reEnabled"] = new Dictionary<string, object?> { ["enabled"] = ts.Enabled, ["consentAskedAgain"] = false, ["installed"] = host.Installed?.Display };
        sw.Close();
        await WaitForAsync(() => _playerSettingsWindow is null, 5000);
        await Within(host.StartIfWorkPendingAsync(), 90, "host.StartIfWorkPendingAsync");
        await WaitForAsync(() => host.Status == TorlinkStatus.Running, 20000);
        await Within(host.RefreshAsync(), 90, "host.RefreshAsync");
        await WaitForAsync(() => host.Snapshot.Active.Any(i => i.State == DownloadState.Downloading), 60000);
        r["resumedAfterReEnable"] = host.Snapshot.Active.Any(i => i.State == DownloadState.Downloading);

        // ---- 7) pad procesa usred preuzimanja (nakon što je napredak stvarno krenuo: pad tijekom podizanja Torlink sam tretira kao sumnjiv i pauzira stavke) ----
        await WaitForAsync(() => host.Snapshot.Active.Any(i => i.Percent >= 5), 120000);
        await Task.Delay(8000); // Torlinkov boot marker se briše tek 4 s nakon podizanja; ranije "slučajno" ubijanje bi bilo pad pri podizanju
        int pid2 = host.Supervisor.ProcessId ?? 0;
        double before = host.Snapshot.Active.FirstOrDefault()?.Percent ?? 0;
        Process.GetProcessById(pid2).Kill(entireProcessTree: true);
        await WaitForAsync(() => host.Supervisor.ProcessId is int p && p != pid2 && host.Status == TorlinkStatus.Running, 30000);
        await Within(host.RefreshAsync(), 90, "host.RefreshAsync");
        await WaitForAsync(() => host.Snapshot.Active.Any(i => i.State == DownloadState.Downloading && i.Percent >= before - 3), 150000);
        r["crash"] = new Dictionary<string, object?>
        {
            ["restarted"] = host.Supervisor.ProcessId is int q && q != pid2, ["status"] = host.Status.ToString(), ["percentBefore"] = before,
            ["percentAfter"] = host.Snapshot.Active.FirstOrDefault()?.Percent, ["downloading"] = host.Snapshot.Active.Any(i => i.State == DownloadState.Downloading),
        };
        OpenTorlink();
        await Task.Delay(500);
        r["crashBanner"] = page.BannerForTests;
        CloseTorlink();

        // ---- 8) ažuriranje: uspješno (r2), pa neuspješno (r3 s neispravnim node.exe) s automatskim vraćanjem ----
        if (zip2 is { Length: > 0 } && zip3 is { Length: > 0 })
        {
            gh.Releases.Add(("torlink-runtime-1.11.1-r2", zip2));
            gh.UpstreamVersion = "1.11.1";
            ShowPlayerSettings(SettingsTab.Torlink);
            await WaitForAsync(() => _playerSettingsWindow is { IsLoaded: true }, 5000);
            sw = _playerSettingsWindow!;
            sw.AskUnsavedOverride = () => UnsavedChoice.Save;
            panel = sw.TorlinkPanelForTests!;
            await Within(panel.ClickCheckForTestsAsync(), 90, "panel.ClickCheckForTestsAsync");
            r["updateAvailable"] = new Dictionary<string, object?> { ["text"] = panel.UpdateStatusForTests, ["button"] = panel.UpdateButtonVisibleForTests };
            await Within(panel.ClickUpdateForTestsAsync(), 90, "panel.ClickUpdateForTestsAsync");
            await Task.Delay(500);
            r["updateOk"] = new Dictionary<string, object?>
            {
                ["installed"] = host.Installed?.Display, ["text"] = panel.UpdateStatusForTests, ["rollbackButton"] = panel.RollbackButtonVisibleForTests,
                ["status"] = host.Status.ToString(), ["stateKept"] = File.Exists(new TorlinkPaths(AppPaths.DataDir).QueueFile),
            };
            r["settingsUpdatedShot"] = SaveWindowPng("torlink_settings_updated", sw);

            gh.Releases.Add(("torlink-runtime-1.11.1-r3", zip3));
            await Within(panel.ClickCheckForTestsAsync(), 90, "panel.ClickCheckForTestsAsync");
            await Within(panel.ClickUpdateForTestsAsync(), 90, "panel.ClickUpdateForTestsAsync");
            await Task.Delay(800);
            await Within(host.EnsureRunningAsync(), 90, "host.EnsureRunningAsync");
            r["updateBrokenRollback"] = new Dictionary<string, object?>
            {
                ["installed"] = host.Installed?.Display, ["text"] = panel.UpdateStatusForTests, ["stillWorks"] = host.Status == TorlinkStatus.Running,
                ["healthy"] = host.Supervisor.Api is not null && await host.Supervisor.Api.HealthAsync() is not null,
                ["supervisorError"] = host.Supervisor.LastError,
            };

            // GitHub nedostupan: provjera javlja grešku, Torlink radi dalje
            gh.Down = true;
            await Within(panel.ClickCheckForTestsAsync(), 90, "panel.ClickCheckForTestsAsync");
            r["githubDown"] = new Dictionary<string, object?> { ["text"] = panel.UpdateStatusForTests, ["stillRunning"] = host.Status == TorlinkStatus.Running };
            gh.Down = false;

            // ručno vraćanje na prethodnu
            await Within(panel.ClickRollbackForTestsAsync(), 90, "panel.ClickRollbackForTestsAsync");
            r["manualRollback"] = new Dictionary<string, object?> { ["installed"] = host.Installed?.Display, ["text"] = panel.UpdateStatusForTests };
            sw.Close();
            await WaitForAsync(() => _playerSettingsWindow is null, 5000);
        }

        // ---- 9) resursi i stanje za izlaz ----
        await Within(host.EnsureRunningAsync(), 90, "host.EnsureRunningAsync");
        int pidEnd = host.Supervisor.ProcessId ?? 0;
        r["resources"] = new Dictionary<string, object?> { ["nodeMb"] = MbOf(pidEnd), ["playerMb"] = MbOf(Environment.ProcessId), ["pid"] = pidEnd };
        r["exitPid"] = pidEnd;
        r["done"] = true;
        return;
    }

    /// <summary>Drugo pokretanje playera nad istom mapom: nedovršeno preuzimanje se nastavlja samo, bez otvaranja stranice (<c>--diag-ui-test torlinkresume</c>).</summary>
    private async Task<Dictionary<string, object?>> TorlinkResumeUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (_torlink is null)
        {
            r["error"] = "Torlink nije dostupan";
            return r;
        }

        TorlinkHost host = _torlink.Host;
        if (_vm.HasMedia)
        {
            GoHome();
        }

        await WaitForAsync(() => WelcomePanel.IsVisible, 8000);
        r["enabled"] = _torlink.Settings.Enabled;
        r["pageOpened"] = _torlinkView;
        await WaitForAsync(() => host.Status == TorlinkStatus.Running, 30000);
        await host.RefreshAsync();
        await WaitForAsync(() => host.Snapshot.Active.Any(i => i.State == DownloadState.Downloading), 60000);
        r["autoStarted"] = host.Status == TorlinkStatus.Running;
        r["resumedDownloading"] = host.Snapshot.Active.Any(i => i.State == DownloadState.Downloading);
        r["percent"] = host.Snapshot.Active.FirstOrDefault()?.Percent;
        r["chipVisible"] = _torlinkChip!.IsVisible;
        r["homeButtonVisible"] = _torlinkHomeButton!.IsVisible;
        r["pid"] = host.Supervisor.ProcessId;
        await host.ControlAsync(BigBuckBunnyHash, "delete");
        await Task.Delay(1000);
        r["afterDeleteActive"] = host.Snapshot.Active.Count;
        r["done"] = true;
        return r;
    }
}
#endif
