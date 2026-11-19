using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Library;
using JebaitedPlayer.Mpv;
using JebaitedPlayer.Views;
using static JebaitedPlayer.Interop.NativeMethods;

namespace JebaitedPlayer;

/// <summary>
/// Dijagnostika sučelja stvarnim mišem (UPGRADE 3, <c>--diag-ui-test</c>): kursor se pomiče i klikće kroz Windows
/// (SetCursorPos + SendInput), isto kao kad korisnik klikne, pa test prolazi kroz pravi put (prozor s kontrolama koji
/// se ne aktivira, hvatanje miša, skrivanje kontrola). Rezultati idu u JSON dijagnostike; skripta ih provjerava.
/// </summary>
public partial class MainWindow
{
    private void InitializeUiTests()
    {
        if (_diag is null || _options.DiagUiTest is not { } scenario)
        {
            return;
        }

        _diag.UiTest = async () =>
        {
            GetCursorPos(out POINT saved);
            try
            {
                return scenario switch
                {
                    "timeline" => await TimelineUiTestAsync(),
                    "home" => await HomeUiTestAsync(fullscreen: false),
                    "home-fullscreen" => await HomeUiTestAsync(fullscreen: true),
                    "settings" => await SettingsUiTestAsync(),
                    "equalizer" => await EqualizerUiTestAsync(),
                    "watched" => await WatchedUiTestAsync(),
                    "backup" => await BackupUiTestAsync(),
                    "mini" => await MiniUiTestAsync(),
                    _ => new Dictionary<string, object?> { ["error"] = "nepoznat scenarij " + scenario },
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object?> { ["exception"] = ex.ToString() };
            }
            finally
            {
                MouseButton(false);
                SetCursorPos(saved.X, saved.Y);
            }
        };
    }

    private async Task<Dictionary<string, object?>> TimelineUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.Duration > 0 && _overlay != null, 8000);
        double duration = _vm.Duration;
        r["duration"] = duration;
        r["width"] = ActualWidth;
        r["fullscreen"] = _vm.IsFullscreen;

        // 1) Video se reproducira: klik na bilo koje mjesto skoči tamo i ne mijenja pauzu.
        _vm.SetPaused(false);
        await Task.Delay(600);
        r["playingClicks"] = await ClickSeriesAsync([0.25, 0.5, 0.75, 0.1, 0.9], duration);

        // 2) Video je pauziran: klik ga ne pokreće, a pozicija je precizna (bez daljnje reprodukcije).
        _vm.SetPaused(true);
        await Task.Delay(600);
        r["pausedClicks"] = await ClickSeriesAsync([0.33, 0.66, 0.05], duration);

        // 3) Povlačenje i dalje radi: od 20 % do 60 %, pozicija prati miš, na kraju precizno.
        RegisterActivity();
        await Task.Delay(300);
        Point from = _overlay!.TimelineScreenPoint(0.2);
        Point to = _overlay.TimelineScreenPoint(0.6);
        SetCursorPos((int)Math.Round(from.X), (int)Math.Round(from.Y));
        await Task.Delay(250);
        MouseButton(true);
        var during = new List<double>();
        var bubbles = new List<string?>();
        for (int i = 1; i <= 12; i++)
        {
            double x = from.X + (to.X - from.X) * i / 12.0;
            SetCursorPos((int)Math.Round(x), (int)Math.Round(from.Y));
            await Task.Delay(60);
            during.Add(Math.Round(_vm.Position, 2));
            bubbles.Add(_overlay.SeekPreviewText);
        }

        MouseButton(false);
        await Task.Delay(900);
        double? afterDrag = await MpvDoubleAsync("time-pos");
        r["drag"] = new Dictionary<string, object?>
        {
            ["expected"] = Math.Round(0.6 * duration, 2),
            ["positionsDuringDrag"] = during,
            ["bubblesDuringDrag"] = bubbles,
            ["timePosAfter"] = afterDrag,
            ["error"] = afterDrag is double d ? Math.Round(d - 0.6 * duration, 3) : null,
            ["pausedAfter"] = _vm.IsPaused,
            ["scrubbingAfter"] = _vm.IsScrubbing,
        };
        _vm.SetPaused(false);
        return r;
    }

    private async Task<List<Dictionary<string, object?>>> ClickSeriesAsync(double[] fractions, double duration)
    {
        var clicks = new List<Dictionary<string, object?>>();
        foreach (double f in fractions)
        {
            RegisterActivity();
            await Task.Delay(300);
            bool pausedBefore = _vm.IsPaused;
            Point pt = _overlay!.TimelineScreenPoint(f);
            SetCursorPos((int)Math.Round(pt.X), (int)Math.Round(pt.Y));
            await Task.Delay(300);
            string? hover = _overlay.SeekPreviewText;
            double? hoverSeconds = _overlay.SeekPreviewSeconds;
            long t0 = Environment.TickCount64;
            MouseButton(true);
            await Task.Delay(40);
            MouseButton(false);
            await Task.Delay(700);
            double? pos = await MpvDoubleAsync("time-pos");
            double elapsed = (Environment.TickCount64 - t0) / 1000.0;
            double expected = f * duration;
            clicks.Add(new Dictionary<string, object?>
            {
                ["fraction"] = f,
                ["expected"] = Math.Round(expected, 2),
                ["hoverText"] = hover,
                ["expectedText"] = TimeFormat.Format(expected, duration >= 3600),
                ["timePos"] = pos is double p1 ? Math.Round(p1, 3) : null,
                ["error"] = pos is double p2 ? Math.Round(p2 - expected, 3) : null,
                ["hoverSeconds"] = hoverSeconds is double h ? Math.Round(h, 3) : null,
                ["errorVsHover"] = pos is double p3 && hoverSeconds is double h2 ? Math.Round(p3 - h2, 3) : null,
                ["secondsSinceClick"] = Math.Round(elapsed, 2),
                ["pausedBefore"] = pausedBefore,
                ["pausedAfter"] = _vm.IsPaused,
                ["controlsVisible"] = _overlay.IsVisible,
            });
        }

        return clicks;
    }

    private async Task<Dictionary<string, object?>> HomeUiTestAsync(bool fullscreen)
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.Duration > 0 && _topBar != null, 8000);
        string path = _vm.CurrentPath ?? string.Empty;
        double duration = _vm.Duration;
        r["duration"] = duration;
        if (fullscreen)
        {
            SetFullscreen(true);
            await Task.Delay(1000);
        }

        r["fullscreenBefore"] = _vm.IsFullscreen;
        _vm.SeekAbsolute(Math.Round(duration * 0.4, 1), exact: true);
        _vm.SetPaused(false);
        await Task.Delay(1500);

        // Home je gore lijevo na videu i vidi se zajedno s donjim kontrolama.
        TryGetVideoAreaRect(out int vx, out int vy, out int vw, out int vh);
        r["videoRect"] = new[] { vx, vy, vw, vh };
        var center = new POINT { X = vx + vw / 2, Y = vy + vh / 2 };
        SetCursorPos(center.X, center.Y);
        await Task.Delay(400);
        Rect home = _topBar!.HomeButtonScreenRect();
        r["homeRect"] = new[] { Math.Round(home.X), Math.Round(home.Y), Math.Round(home.Width), Math.Round(home.Height) };
        r["homeOffsetFromVideoTopLeft"] = new[] { Math.Round(home.X - vx), Math.Round(home.Y - vy) };
        r["visibleAfterMove"] = Visibility2();

        // Bez pomicanja miša kontrole se skriju, a s njima i Home.
        await Task.Delay(HideControlsAfter + TimeSpan.FromSeconds(1.2));
        r["visibleAfterIdle"] = Visibility2();

        // Pomak miša: obje se opet pokažu.
        SetCursorPos(center.X + 40, center.Y + 15);
        await Task.Delay(500);
        r["visibleAfterSecondMove"] = Visibility2();

        // Pravi klik na Home.
        double? before = await MpvDoubleAsync("time-pos");
        System.Windows.Point hc = _topBar.HomeButtonScreenCenter();
        SetCursorPos((int)Math.Round(hc.X), (int)Math.Round(hc.Y));
        await Task.Delay(300);
        MouseButton(true);
        await Task.Delay(40);
        MouseButton(false);
        await Task.Delay(1500);
        WatchEntry? saved = _store?.Get(path);
        r["afterHome"] = new Dictionary<string, object?>
        {
            ["hasMedia"] = _vm.HasMedia,
            ["welcomeVisible"] = WelcomePanel.IsVisible,
            ["videoVisible"] = Video.Visibility == Visibility.Visible,
            ["windowVisible"] = IsVisible,
            ["fullscreen"] = _vm.IsFullscreen,
            ["bottomControlsVisible"] = _overlay?.IsVisible,
            ["homeVisible"] = _topBar.IsVisible,
            ["windowTitle"] = Title,
            ["positionBeforeClick"] = before is double b ? Math.Round(b, 2) : null,
            ["savedPosition"] = saved is null ? null : Math.Round(saved.Position, 2),
            ["savedWatched"] = saved?.Watched,
            ["recent"] = _vm.Library.Recent.Select(x => $"{x.Title} | {x.Detail}").ToArray(),
        };

        // Ponovno otvaranje: nudi se nastavak od spremljene pozicije, a "Nastavi" kreće točno odande.
        OpenFiles([path]);
        await WaitForAsync(() => _vm.Library.HasResumeOffer, 10000);
        r["resumeOffer"] = _vm.Library.ResumeOffer is double offer ? Math.Round(offer, 2) : null;
        _vm.Library.ResumeContinue();
        await Task.Delay(1500);
        double? resumed = await MpvDoubleAsync("time-pos");
        r["timePosAfterResume"] = resumed is double rp ? Math.Round(rp, 2) : null;
        r["pausedAfterResume"] = _vm.IsPaused;
        return r;

        Dictionary<string, object?> Visibility2() => new()
        {
            ["bottom"] = _overlay?.IsVisible,
            ["home"] = _topBar?.IsVisible,
        };
    }

    /// <summary>
    /// Postavke (UPGRADE 3): svaka kartica se otvara, promjene zvuka i slike stižu u mpv odmah, korak premotavanja,
    /// vrijeme skrivanja kontrola i tri načina nastavka gledanja. Postavke se u dijagnostici ne spremaju.
    /// </summary>
    private async Task<Dictionary<string, object?>> SettingsUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.Duration > 0, 8000);
        string path = _vm.CurrentPath ?? string.Empty;
        double duration = _vm.Duration;
        ISettingsHost host = this;

        // 1) Sve kartice postavki se otvaraju i imaju sadržaj.
        var tabs = new Dictionary<string, object?>();
        foreach (SettingsTab tab in Enum.GetValues<SettingsTab>())
        {
            ShowPlayerSettings(tab);
            await Task.Delay(400);
            PlayerSettingsWindow w = _playerSettingsWindow!;
            var content = (w.Tabs.SelectedItem as System.Windows.Controls.TabItem)?.Content as FrameworkElement;
            tabs[tab.ToString()] = new Dictionary<string, object?>
            {
                ["selected"] = w.SelectedTab.ToString(),
                ["contentHeight"] = Math.Round(content?.ActualHeight ?? 0),
                ["header"] = (w.Tabs.SelectedItem as System.Windows.Controls.TabItem)?.Header?.ToString(),
            };
        }

        r["tabs"] = tabs;
        _playerSettingsWindow?.Close();
        r["audioDevices"] = (await host.GetAudioDevicesAsync()).Select(d => $"{d.Name} | {d.Description}").ToArray();

        // 2) Zvuk i slika: promjena → mpv odmah.
        _settings.HardwareDecoding = false;
        _settings.VideoQuality = "high";
        _settings.NormalizeAudio = true;
        _settings.AudioDelayMs = 250;
        _settings.AudioLanguage = "en";
        _settings.HdrPassthrough = true;
        host.ApplyPlaybackSettings();
        await Task.Delay(1200);
        r["changed"] = await MpvSnapshotAsync();
        _settings.HardwareDecoding = true;
        _settings.VideoQuality = "fast";
        _settings.NormalizeAudio = false;
        _settings.AudioDelayMs = 0;
        _settings.AudioLanguage = null;
        _settings.HdrPassthrough = false;
        host.ApplyPlaybackSettings();
        await Task.Delay(1200);
        r["fast"] = await MpvSnapshotAsync();
        _settings.VideoQuality = "balanced";
        host.ApplyPlaybackSettings();
        await Task.Delay(1200);
        r["balanced"] = await MpvSnapshotAsync();
        _vm.Extras.ApplyAspect("4:3", announce: false);
        await Task.Delay(300);
        r["aspect"] = await Task.Run(() => _player?.GetPropertyString("video-aspect-override"));
        _vm.Extras.ApplyAspect("auto", announce: false);

        // 3) Korak premotavanja: 10 s (Ctrl = 60 s).
        _vm.SetPaused(true);
        _vm.SeekAbsolute(600, exact: true);
        await Task.Delay(800);
        _settings.SeekStepSeconds = 10;
        HandleShortcut(System.Windows.Input.Key.Right, System.Windows.Input.ModifierKeys.None);
        await Task.Delay(800);
        double? afterRight = await MpvDoubleAsync("time-pos");
        HandleShortcut(System.Windows.Input.Key.Left, System.Windows.Input.ModifierKeys.Control);
        await Task.Delay(800);
        double? afterCtrlLeft = await MpvDoubleAsync("time-pos");
        r["seekStep"] = new Dictionary<string, object?> { ["afterRight"] = afterRight, ["afterCtrlLeft"] = afterCtrlLeft };
        _settings.SeekStepSeconds = 5;

        // 4) Skrivanje kontrola nakon 6 s: vidljive nakon 4 s, skrivene nakon 7 s (video se reproducira).
        _settings.ControlsHideSeconds = 6;
        _vm.SetPaused(false);
        TryGetVideoAreaRect(out int vx, out int vy, out int vw, out int vh);
        SetCursorPos(vx + vw / 2, vy + vh / 2);
        await Task.Delay(300);
        SetCursorPos(vx + vw / 2 + 20, vy + vh / 2 + 10);
        await Task.Delay(4000);
        bool visibleAt4 = _overlay?.IsVisible == true && _topBar?.IsVisible == true;
        await Task.Delay(3200);
        bool hiddenAt7 = _overlay?.IsVisible == false && _topBar?.IsVisible == false;
        r["hideDelay"] = new Dictionary<string, object?> { ["visibleAfter4s"] = visibleAt4, ["hiddenAfter7s"] = hiddenAt7 };
        _settings.ControlsHideSeconds = 2.5;

        // 5) Nastavak gledanja: "resume" nastavi sam, "start" krene od početka, "ask" ponudi karticu.
        var resume = new Dictionary<string, object?>();
        foreach (string mode in new[] { "resume", "start", "ask" })
        {
            _vm.SeekAbsolute(Math.Round(duration * 0.3, 1), exact: true);
            _vm.SetPaused(false);
            await Task.Delay(1200);
            GoHome();
            await Task.Delay(800);
            double saved = _store?.Get(path)?.Position ?? -1;
            _vm.Library.ResumeMode = mode;
            OpenFiles([path]);
            await WaitForAsync(() => _vm.HasMedia && _vm.Duration > 0, 8000);
            await Task.Delay(2000);
            double? pos = await MpvDoubleAsync("time-pos");
            resume[mode] = new Dictionary<string, object?>
            {
                ["saved"] = Math.Round(saved, 2),
                ["timePos"] = pos is double p ? Math.Round(p, 2) : null,
                ["offer"] = _vm.Library.ResumeOffer,
                ["paused"] = _vm.IsPaused,
            };
            if (_vm.Library.HasResumeOffer)
            {
                _vm.Library.ResumeContinue();
            }
        }

        _vm.Library.ResumeMode = _settings.ResumeMode;
        r["resume"] = resume;
        return r;
    }

    private async Task<Dictionary<string, string?>> MpvSnapshotAsync()
    {
        MpvPlayer? p = _player;
        return p is null
            ? []
            : await Task.Run(() => new[]
                {
                    "hwdec", "hwdec-current", "scale", "dscale", "deband", "af", "audio-delay", "alang", "target-colorspace-hint",
                    "audio-device", "current-ao",
                }
                .ToDictionary(n => n, n => p.GetPropertyString(n)));
    }

    private async Task<double?> MpvDoubleAsync(string name) =>
        _player is { } p ? await Task.Run(() => p.GetPropertyDouble(name)) : null;

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs)
    {
        long end = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < end)
        {
            await Task.Delay(100);
        }
    }
}
