using System.Windows.Input;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.ViewModels;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Dijagnostički test V1 Mini Music Playera (--diag-ui-test mini) na testnom albumu od tri pjesme:
/// prebacivanje bez prekida, naslov iz oznaka, VU metar, ploče, sljedeća pjesma odmah, uska traka, veličina, Ctrl+M, CPU.
/// </summary>
public partial class MainWindow
{
    private async Task<Dictionary<string, object?>> MiniUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.Duration > 0 && _vm.Library.Current != null, 8000);
        LibraryController lib = _vm.Library;
        string albumDir = System.IO.Path.GetDirectoryName(_vm.CurrentPath!)!;
        r["playlist"] = lib.Playlist.Select(p => p.Label).ToArray();
        _vm.SetPaused(false);
        await Task.Delay(800);
        r["cpuFullPlayer"] = await CpuPercentAsync(5000);

        // 1) Prebacivanje u mini player: glavni prozor i kontrole nestaju, glazba svira dalje, VU filtar je u lancu.
        double t0 = await MpvDoubleAsync("time-pos") ?? 0;
        ShowMiniPlayer();
        await Task.Delay(1500);
        MiniPlayerWindow mini = _mini!;
        double t1 = await MpvDoubleAsync("time-pos") ?? 0;
        r["switch"] = new Dictionary<string, object?>
        {
            ["mainVisible"] = IsVisible,
            ["miniVisible"] = mini.IsVisible,
            ["overlayVisible"] = _overlayVisible,
            ["advanced"] = Math.Round(t1 - t0, 2),
            ["paused"] = _vm.IsPaused,
            ["af"] = await AfAsync(),
            ["title"] = mini.MarqueeTextValue,
            ["vu"] = new[] { Math.Round(mini.VuLevels.Left, 1), Math.Round(mini.VuLevels.Right, 1) },
            ["vuRaw"] = await Task.Run(() => _player?.GetPropertyString("af-metadata/vu")),
            ["size"] = new[] { Math.Round(mini.ActualWidth), Math.Round(mini.ActualHeight) },
        };
        r["cpuMiniPlayer"] = await CpuPercentAsync(5000);

        // 2) Ploče EQ i popis.
        mini.SetPanels(equalizer: true, playlist: true);
        await Task.Delay(600);
        r["panels"] = new Dictionary<string, object?>
        {
            ["eqColumns"] = mini.Equalizer.Bands.Children.OfType<System.Windows.Controls.StackPanel>().Count(),
            ["playlistRows"] = mini.PlaylistItems.Items.Count,
            ["size"] = new[] { Math.Round(mini.ActualWidth), Math.Round(mini.ActualHeight) },
        };
        r["fullScreenshot"] = SaveWindowPng("mini_full", mini);
        mini.SetPanels(equalizer: false, playlist: false);

        // 3) Sljedeća pjesma tipkom, zatim kraj pjesme: treća kreće odmah, bez kartice i odbrojavanja.
        lib.PlayNext();
        await WaitForAsync(() => lib.Current is { } c && lib.Playlist.IndexOf(c) == 1 && _vm.Duration > 0 && !_vm.IsLoading, 6000);
        await Task.Delay(500);
        r["afterNext"] = lib.Current?.Label;
        _vm.SeekAbsolute(_vm.Duration - 1.0, exact: true);
        long seekAt = Environment.TickCount64;
        await WaitForAsync(() => lib.Current is { } c && lib.Playlist.IndexOf(c) == 2 && _vm.Duration > 0 && !_vm.IsLoading, 8000);
        r["autoNext"] = new Dictionary<string, object?>
        {
            ["current"] = lib.Current?.Label,
            ["secondsAfterSeek"] = Math.Round((Environment.TickCount64 - seekAt) / 1000.0, 2),
            ["upNextCard"] = lib.UpNext?.Label,
            ["resumeOffer"] = lib.ResumeOffer,
        };

        // 4) Uska traka i ručna veličina.
        mini.SetCompact(true);
        await Task.Delay(600);
        double compactWidth = mini.ActualWidth;
        r["compact"] = new Dictionary<string, object?> { ["size"] = new[] { Math.Round(mini.ActualWidth), Math.Round(mini.ActualHeight) }, ["title"] = mini.MarqueeTextValue };
        r["compactScreenshot"] = SaveWindowPng("mini_compact", mini);
        mini.SetScale(1.5);
        await Task.Delay(500);
        r["scaled"] = new Dictionary<string, object?> { ["scale"] = mini.Scale, ["widthRatio"] = Math.Round(mini.ActualWidth / compactWidth, 2), ["saved"] = _settings.MiniPlayer.Scale };
        mini.SetScale(9);
        r["maxScale"] = mini.Scale;
        mini.SetScale(1);
        mini.SetCompact(false);

        // 5) Ctrl+M: natrag na puni player, glazba svira dalje, VU filtar maknut.
        await Task.Delay(400);
        double t2 = await MpvDoubleAsync("time-pos") ?? 0;
        bool handled = HandleShortcut(Key.M, ModifierKeys.Control);
        await Task.Delay(1200);
        double t3 = await MpvDoubleAsync("time-pos") ?? 0;
        r["back"] = new Dictionary<string, object?>
        {
            ["handled"] = handled,
            ["mainVisible"] = IsVisible,
            ["miniVisible"] = mini.IsVisible,
            ["advanced"] = Math.Round(t3 - t2, 2),
            ["paused"] = _vm.IsPaused,
            ["af"] = await AfAsync(),
        };

        // --- 1.4.1 ---------------------------------------------------------------------------------------------
        // 6) Izgledi VU metra i font: "off" miče i mjerenje iz lanca.
        ShowMiniPlayer();
        await Task.Delay(800);
        var vuStyles = new Dictionary<string, object?>();
        foreach (string style in MiniPlayerSettings.VuStyles)
        {
            mini.SetVuStyle(style);
            await Task.Delay(900);
            vuStyles[style] = new Dictionary<string, object?>
            {
                ["af"] = await AfAsync(),
                ["level"] = Math.Round(mini.VuLevels.Left, 1),
            };
            if (style == "led")
            {
                mini.SetFont("digital");
                await Task.Delay(400);
                r["ledScreenshot"] = SaveWindowPng("mini_led_digital", mini);
            }
        }

        r["vuStyles"] = vuStyles;
        r["font"] = mini.TimeText.FontFamily.Source;
        mini.SetVuStyle("bars");
        mini.SetFont("lcd");

        // 7) Odvojeni equalizeri: glazba Bass, filmovi isključeni.
        _settings.Equalizer.Enabled = false;
        _settings.MusicEqualizer.Enabled = true;
        _settings.MusicEqualizer.Preset = "bass";
        ((ISettingsHost)this).ApplyPlaybackSettings();
        mini.BindEqualizer();
        await Task.Delay(700);
        r["musicEq"] = new Dictionary<string, object?>
        {
            ["musicMode"] = MpvConfig.MusicMode,
            ["af"] = await AfAsync(),
            ["panelBoundToMusic"] = ReferenceEquals(mini.Equalizer.Bound, _settings.MusicEqualizer),
            ["title"] = mini.EqTitle.Text,
        };

        // 8) Automatsko prebacivanje kad korisnik sam otvori: film → puni player (filmski EQ), glazba → mini player.
        _diagAutoSwitch = true;
        string video = System.IO.Path.GetFullPath(System.IO.Path.Combine(albumDir, "..", "..", "ciklus1", "Kratki.Film.2026.mkv"));
        OpenFiles([video]);
        await WaitForAsync(() => _vm.Duration > 0 && !_vm.IsLoading && string.Equals(_vm.CurrentPath, video, StringComparison.OrdinalIgnoreCase), 8000);
        await Task.Delay(800);
        r["openVideo"] = new Dictionary<string, object?>
        {
            ["mainVisible"] = IsVisible, ["miniVisible"] = mini.IsVisible, ["musicMode"] = MpvConfig.MusicMode, ["af"] = await AfAsync(),
        };
        string song = System.IO.Path.Combine(albumDir, "02 Druga.flac");
        OpenFiles([song]);
        await WaitForAsync(() => _vm.Duration > 0 && !_vm.IsLoading && string.Equals(_vm.CurrentPath, song, StringComparison.OrdinalIgnoreCase), 8000);
        await Task.Delay(800);
        r["openMusic"] = new Dictionary<string, object?>
        {
            ["mainVisible"] = IsVisible, ["miniVisible"] = mini.IsVisible, ["musicMode"] = MpvConfig.MusicMode, ["af"] = await AfAsync(),
            ["playlist"] = lib.Playlist.Count,
        };

        // 9) Položaj: novi mini prozor (kao nakon ponovnog pokretanja) otvara se na zapamćenom mjestu.
        mini.Left = 210;
        mini.Top = 160;
        await Task.Delay(200);
        var again = new MiniPlayerWindow(_vm, _settings, this);
        r["position"] = new Dictionary<string, object?> { ["saved"] = new[] { _settings.MiniPlayer.Left, _settings.MiniPlayer.Top }, ["reopened"] = new[] { again.Left, again.Top } };
        again.AllowClose = true;
        again.Close();
        return r;
    }
}
