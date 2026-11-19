using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Library;
using JebaitedPlayer.ViewModels;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Dijagnostički testovi ciklusa 1 u pravom playeru (--diag-ui-test equalizer | watched).
/// Postavke se u dijagnostici ne spremaju, a baza je zasebna (--diag-db).
/// </summary>
public partial class MainWindow
{
    /// <summary>V2 Equalizer: kartica Zvuk, svi preseti u mpv-u bez prekida reprodukcije, stvarna promjena razine, CPU.</summary>
    private async Task<Dictionary<string, object?>> EqualizerUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.Duration > 0, 8000);
        _player?.SetPropertyAsync("mute", "yes"); // testni ton 440 Hz se ne čuje; astats mjeri prije izlazne glasnoće
        ISettingsHost host = this;
        _vm.SetPaused(false);
        await Task.Delay(1500);

        // 1) Kartica Zvuk: 11 klizača (preamp + 10 pojaseva), uključivanje i pomicanje klizača iz sučelja.
        ShowPlayerSettings(SettingsTab.Audio);
        await Task.Delay(700);
        PlayerSettingsWindow w = _playerSettingsWindow!;
        List<StackPanel> columns = w.EqualizerPanel.Bands.Children.OfType<StackPanel>().ToList();
        r["columns"] = columns.Count;
        r["labels"] = columns.Select(c => (c.Children[2] as TextBlock)?.Text).ToArray();
        r["panelEnabledBefore"] = w.EqualizerPanel.Panel.IsEnabled;
        w.EqualizerPanel.EnabledBox.IsChecked = true;
        await Task.Delay(800);
        r["panelEnabledAfter"] = w.EqualizerPanel.Panel.IsEnabled;
        r["afFlat"] = await AfAsync();
        double t0 = await MpvDoubleAsync("time-pos") ?? 0;
        ((Slider)columns[1].Children[1]).Value = 6;
        await Task.Delay(1000);
        double t1 = await MpvDoubleAsync("time-pos") ?? 0;
        r["slider"] = new Dictionary<string, object?>
        {
            ["preset"] = _settings.Equalizer.Preset,
            ["presetButton"] = w.EqualizerPanel.PresetButton.Content?.ToString(),
            ["af"] = await AfAsync(),
            ["advanced"] = Math.Round(t1 - t0, 2),
            ["paused"] = _vm.IsPaused,
        };
        r["settingsScreenshot"] = SaveWindowPng("equalizer_settings", w);
        w.Close();

        // 2) Svaki preset stiže u mpv, a reprodukcija teče dalje (nema pauze ni zastoja).
        var presets = new Dictionary<string, object?>();
        foreach (string id in Equalizer.Presets.Select(p => p.Id).Append(Equalizer.Custom))
        {
            _settings.Equalizer.Preset = id;
            host.ApplyPlaybackSettings();
            double a = await MpvDoubleAsync("time-pos") ?? 0;
            await Task.Delay(800);
            double b = await MpvDoubleAsync("time-pos") ?? 0;
            presets[id] = new Dictionary<string, object?>
            {
                ["expected"] = MpvConfig.AudioFilter(_settings),
                ["af"] = await AfAsync(),
                ["advanced"] = Math.Round(b - a, 2),
                ["paused"] = _vm.IsPaused,
                ["ao"] = await Task.Run(() => _player?.GetPropertyString("current-ao")),
            };
        }

        r["presets"] = presets;

        // 3) Zajedno s ujednačavanjem glasnoće: jedan graf, prvo equalizer pa dynaudnorm.
        _settings.Equalizer.Preset = "rock";
        _settings.NormalizeAudio = true;
        host.ApplyPlaybackSettings();
        await Task.Delay(800);
        r["withNormalize"] = await AfAsync();
        _settings.NormalizeAudio = false;

        // 4) Stvarna promjena zvuka: testni film ima ton 440 Hz; pojas 500 Hz na −12 dB mora ga primjetno stišati.
        r["level"] = await MeasureEqLevelAsync();

        // 5) CPU: isti video bez equalizera i s Rock presetom (svih 10 pojaseva), naizmjence.
        var cpu = new List<Dictionary<string, object?>>();
        for (int round = 0; round < 2; round++)
        {
            foreach (bool on in new[] { false, true })
            {
                _settings.Equalizer.Enabled = on;
                host.ApplyPlaybackSettings();
                await Task.Delay(1000);
                cpu.Add(new Dictionary<string, object?> { ["eq"] = on, ["cpuPercentOfOneCore"] = await CpuPercentAsync(6000) });
            }
        }

        r["cpu"] = cpu;
        _settings.Equalizer.Enabled = false;
        host.ApplyPlaybackSettings();
        await Task.Delay(600);
        r["afOff"] = await AfAsync();
        r["pausedAtEnd"] = _vm.IsPaused;
        return r;
    }

    private async Task<Dictionary<string, object?>> MeasureEqLevelAsync()
    {
        var result = new Dictionary<string, object?>();
        if (_player is not { } p)
        {
            return result;
        }

        const string stats = "@st:lavfi=[astats=metadata=1:reset=1]";
        var cut = new EqualizerSettings { Enabled = true, Preset = Equalizer.Custom, CustomGains = [0, 0, 0, 0, -12, 0, 0, 0, 0, 0] };
        foreach (var (name, filter) in new[] { ("off", stats), ("cut500", $"lavfi=[{Equalizer.BuildFilters(cut)}],{stats}") })
        {
            p.SetPropertyAsync("af", filter);
            await Task.Delay(1200);
            var samples = new List<double>();
            for (int i = 0; i < 8; i++)
            {
                string? v = await Task.Run(() => p.GetPropertyString("af-metadata/st/by-key/lavfi.astats.Overall.RMS_level"));
                if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double db))
                {
                    samples.Add(db);
                }

                await Task.Delay(150);
            }

            result[name] = samples.Count > 0 ? Math.Round(samples.Average(), 2) : null;
            if (samples.Count == 0)
            {
                result[name + "Raw"] = await Task.Run(() => p.GetPropertyString("af-metadata/st"));
            }
        }

        if (result["off"] is double off && result["cut500"] is double on)
        {
            result["differenceDb"] = Math.Round(on - off, 2);
        }

        return result;
    }

    private static async Task<double> CpuPercentAsync(int milliseconds)
    {
        using Process me = Process.GetCurrentProcess();
        TimeSpan cpu0 = me.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        await Task.Delay(milliseconds);
        me.Refresh();
        return Math.Round((me.TotalProcessorTime - cpu0).TotalMilliseconds / clock.Elapsed.TotalMilliseconds * 100, 2);
    }

    private async Task<string?> AfAsync() => await Task.Run(() => _player?.GetPropertyString("af"));

    /// <summary>
    /// M6a + M6b na kratkom videu: premotavanje do kraja ne označava pogledano, stvarno gledanje označava,
    /// ponovno otvaranje i zatvaranje na 10 % ne poništava status, a ručna oznaka i dalje radi.
    /// </summary>
    private async Task<Dictionary<string, object?>> WatchedUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.Duration > 0 && _vm.Library.Current != null, 8000);
        _player?.SetPropertyAsync("mute", "yes");
        LibraryController lib = _vm.Library;
        string path = _vm.CurrentPath!;
        double duration = _vm.Duration;
        r["duration"] = duration;

        // 1) Dvije sekunde gledanja, zatim skok na kraj (EOF): NE smije biti pogledano.
        _vm.SetPaused(false);
        await Task.Delay(2000);
        _vm.SeekAbsolute(duration - 1.5, exact: true);
        await WaitForAsync(() => _vm.EofReached, 8000);
        await Task.Delay(800);
        r["skipToEnd"] = Entry(path);

        // 2) Stvarno gledanje od početka do kraja (brzina 4×, da test traje kraće): pogledano.
        _vm.Extras.SetSpeed(4);
        _vm.SeekAbsolute(0, exact: true);
        _vm.SetPaused(false);
        await Task.Delay(1500);
        await WaitForAsync(() => _vm.EofReached, (int)(duration / 4 * 1000) + 10000);
        await Task.Delay(800);
        r["watchedToEnd"] = Entry(path);
        _vm.Extras.SetSpeed(1);

        // 3) Ponovno otvaranje pogledanog videa, gledanje do 10 % i povratak na početni ekran: ostaje pogledano.
        lib.PlayItem(lib.Current!);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Duration > 0, 8000);
        await Task.Delay(500);
        r["reopenStartedAt"] = await MpvDoubleAsync("time-pos");
        r["reopenResumeOffer"] = lib.ResumeOffer;
        _vm.SetPaused(false);
        _vm.SeekAbsolute(duration * 0.10, exact: true);
        await Task.Delay(1500);
        GoHome();
        await Task.Delay(800);
        r["afterReopen"] = Entry(path);
        r["recent"] = lib.Recent.Select(i => new Dictionary<string, object?>
        {
            ["title"] = i.Title, ["detail"] = i.Detail, ["progress"] = Math.Round(i.Progress, 3), ["watched"] = i.Watched,
        }).ToArray();
        r["homeScreenshot"] = SaveWindowPng("watched_home");
        r["watchedText"] = Localization.Loc.T("history.watched");
        r["playlistWatchedText"] = Localization.Loc.T("playlist.watched");

        // 4) Ručna oznaka: nepogledano pa opet pogledano.
        PlaylistItem item = lib.Playlist.First(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        lib.ToggleWatchedCommand.Execute(item);
        r["manualUnwatched"] = Entry(path);
        r["playlistAfterUnwatch"] = new Dictionary<string, object?> { ["watched"] = item.Watched, ["progress"] = item.Progress };
        lib.ToggleWatchedCommand.Execute(item);
        r["manualWatched"] = Entry(path);
        r["playlistAfterWatch"] = new Dictionary<string, object?> { ["watched"] = item.Watched, ["progress"] = item.Progress, ["text"] = item.ProgressText };
        return r;
    }

    private Dictionary<string, object?>? Entry(string path)
    {
        WatchEntry? e = _store?.Get(path);
        return e is null
            ? null
            : new Dictionary<string, object?>
            {
                ["watched"] = e.Watched,
                ["position"] = Math.Round(e.Position, 2),
                ["coverage"] = WatchCoverage.Parse(e.Coverage) is { } c ? Math.Round(c.Fraction, 3) : null,
                ["watchedProgress"] = Math.Round(e.WatchedProgress, 3),
            };
    }

    /// <summary>Snimka prozora (PNG) pokraj dijagnostičkog izvještaja, za pregled izgleda.</summary>
    private string? SaveWindowPng(string name, Window? window = null)
    {
        if (_options.DiagOut is not { } outFile || (window ?? this).Content is not FrameworkElement root || root.ActualWidth < 1)
        {
            return null;
        }

        // Pozadina prozora nije dio sadržaja: nacrtaj je ispod, da snimka izgleda kao na zaslonu.
        var bmp = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            dc.DrawRectangle((window ?? this).Background, null, bounds);
            dc.DrawRectangle(new VisualBrush(root)
            {
                Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds,
            }, null, bounds);
        }

        bmp.Render(visual);
        string file = Path.Combine(Path.GetDirectoryName(outFile)!, name + ".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using FileStream fs = File.Create(file);
        encoder.Save(fs);
        return file;
    }
}
