using System.Diagnostics;
using System.Globalization;
using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services.Discord;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Dijagnostički test 2.1.12 u pravom playeru (<c>--diag-ui-test v2112</c>, samo uz JEBAITED_DATA_DIR):
/// omot albuma u izgledima 2 i 4 mini playera i na Home ekranu; razina koju bot šalje na Discord uz praćenje glasnoće
/// playera i bez njega. Zvuk je cijelo vrijeme vrlo tih (≈ −60 dBFS i niže), jer hvatanje je prije glavne glasnoće Windowsa.
/// </summary>
public partial class MainWindow
{
    private async Task<Dictionary<string, object?>> V2112UiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 } dataDir ||
            Environment.GetEnvironmentVariable("JEBAITED_V2112_MUSIC") is not { Length: > 0 } music)
        {
            r["error"] = "JEBAITED_DATA_DIR and JEBAITED_V2112_MUSIC are required";
            return r;
        }

        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.3, 12000);
        _player?.SetPropertyAsync("ao-volume", "100"); // Windows pamti glasnoću sesije po aplikaciji
        _player?.SetPropertyAsync("mute", "yes");
        _settings.MiniPlayer.AutoSwitch = false;

        // --- 2) Omot ------------------------------------------------------------------------------------------------
        var covers = new List<Dictionary<string, object?>>();
        foreach ((string file, bool expected) in new[] { ("cover.mp3", true), ("cover.flac", true), ("cover.m4a", true), ("plain.mp3", false), (@"folder\song.mp3", true) })
        {
            string path = System.IO.Path.Combine(music, file);
            if (IsMiniPlayerActive)
            {
                ((IMiniPlayerHost)this).ShowFullPlayer();
            }

            OpenFiles([path]);
            await WaitForAsync(() => string.Equals(_vm.CurrentPath, path, StringComparison.OrdinalIgnoreCase) && _vm.HasMedia && !_vm.IsLoading, 10000);
            _player?.SetPropertyAsync("mute", "yes");
            await Task.Delay(300);

            // Glazba s omotom mora ostati glazba (mpv omot prijavljuje kao video stazu s oznakom albumart).
            string? albumArt = MpvString("current-tracks/video/albumart");
            bool audioOnly = _vm.IsAudioOnly;
            string decision = DiscordAudioGate.Decide(CurrentDiscordInputs() with { IsPaused = false }).ToString();
            GoHome();
            await WaitForAsync(() => string.Equals(Cover.CurrentPath, path, StringComparison.OrdinalIgnoreCase), 5000);
            await Task.Delay(500); // Home se osvježava svakih 200 ms
            bool home = HomeCoverBox.IsVisible && HomeCoverImage.Source is not null;
            string? homeShot = expected ? SaveWindowPng("v2112_home_" + System.IO.Path.GetFileNameWithoutExtension(file)) : null;

            ShowMiniPlayer();
            _mini!.SetDesign("card");
            await Task.Delay(700);
            bool card = _mini.CurrentSkin?.ShowsCover == true;
            string? cardShot = expected && file == "cover.mp3" ? SaveWindowPng("v2112_card", _mini) : null;
            _mini.SetDesign("side");
            await Task.Delay(700);
            bool side = _mini.CurrentSkin?.ShowsCover == true;
            string? sideShot = expected && file == "cover.mp3" ? SaveWindowPng("v2112_side", _mini) : null;
            _mini.SetDesign("classic");
            covers.Add(new Dictionary<string, object?>
            {
                ["file"] = file, ["expected"] = expected, ["hasCover"] = Cover.Current is not null, ["source"] = Cover.CurrentSource,
                ["pixelWidth"] = Cover.CurrentPixelWidth, ["cardShowsCover"] = card, ["sideShowsCover"] = side, ["homeShowsCover"] = home,
                ["homeShot"] = homeShot, ["cardShot"] = cardShot, ["sideShot"] = sideShot,
                ["mpvAlbumArt"] = albumArt, ["audioOnly"] = audioOnly, ["discordDecision"] = decision, ["homeKeptPlaying"] = _vm.HasMedia && _homeView,
            });
            if (file == "plain.mp3")
            {
                r["logoForPlain"] = !card && !side;
                r["homeCoverForPlain"] = home;
            }
        }

        r["covers"] = covers;
        if (IsMiniPlayerActive)
        {
            ((IMiniPlayerHost)this).ShowFullPlayer();
        }

        // --- 5) Glasnoća za Discord (bez Discorda: hvatanje i pojačanje, paketi se odbacuju) --------------------------
        r["discordVolume"] = await DiscordVolumeTestAsync(dataDir, System.IO.Path.Combine(music, "plain.mp3"));
        _player?.SetPropertyAsync("mute", "yes");
        return r;
    }

    private async Task<Dictionary<string, object?>> DiscordVolumeTestAsync(string dataDir, string tone)
    {
        var d = new Dictionary<string, object?>();
        if (!DiscordBot.IsSupported)
        {
            d["skipped"] = "process loopback needs Windows build 20348+";
            return d;
        }

        OpenFiles([tone]);
        await WaitForAsync(() => string.Equals(_vm.CurrentPath, tone, StringComparison.OrdinalIgnoreCase) && _vm.HasMedia && !_vm.IsLoading && _vm.IsAudioOnly, 10000);
        _player?.SetPropertyAsync("loop-file", "inf");
        _player?.SetPropertyAsync("mute", "yes");

        using var bot = new DiscordBot(dataDir, persist: false) { PacketSink = _ => { } };
        DiscordBot? previous = _discord;
        bool followBefore = _settings.DiscordFollowPlayerVolume;
        double discordBefore = _settings.DiscordVolume;
        _discord = bot;
        _vm.PropertyChanged += OnPlayerChangedForDiscord;
        var window = new DiscordWindow(bot, _settings, UpdateDiscordGain);
        try
        {
            bot.Inputs = CurrentDiscordInputs();
            bot.StartLocalAudioForTests();
            d["captureFormat"] = bot.CaptureFormat;

            async Task<double> Level(double playerVolume)
            {
                _vm.Volume = playerVolume; // kroz PlayerViewModel, kao korisnik: pojačanje se mijenja samo
                await Task.Delay(500);
                int peak = 0;
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 900)
                {
                    peak = Math.Max(peak, bot.LastPeak);
                    await Task.Delay(15);
                }

                return peak;
            }

            static double Db(double from, double to) => from <= 0 || to <= 0 ? 99 : 20 * Math.Log10(from / to);

            // Tiho: 20 % u mpv-u ≈ −42 dB, ton −18 dBFS → vrh ≈ −60 dBFS.
            _vm.IsMuted = false;
            _player?.SetPropertyAsync("mute", "no");

            // a) Prati player (zadano): pola glasnoće = ≈ −18 dB i na Discordu.
            window.SetFollowPlayerVolume(true);
            double f20 = await Level(20), f10 = await Level(10);
            d["follow"] = $"20:{f20} 10:{f10}";
            d["followDrop"] = Db(f20, f10);

            // b) Vlastita glasnoća za Discord (20): player 20 → 10 → 7, a Discord ostaje isti.
            window.SetFollowPlayerVolume(false);
            window.SetDiscordVolume(20);
            double i20 = await Level(20), i10 = await Level(10), i7 = await Level(7);
            d["independent"] = $"20:{i20} 10:{i10} 7:{i7}";
            d["independentDrop50"] = Db(i20, i10);
            d["independentDrop25"] = Db(i20, i7);
            d["gainAt7"] = Math.Round(bot.SendGain, 2);

            // c) Discord glasnoća 20 → 10 uz isti player: ≈ −18 dB.
            window.SetDiscordVolume(10);
            double b10 = await Level(10);
            d["botVolumeDrop"] = Db(i10, b10);
            d["settingsSaved"] = !_settings.DiscordFollowPlayerVolume && Math.Abs(_settings.DiscordVolume - 10) < 0.01;

            // d) Utišan player: tišina i bez praćenja (napisano u prozoru).
            _vm.IsMuted = true;
            await Task.Delay(500);
            d["mutedGain"] = bot.SendGain;
            d["sessionVolume"] = MpvString("ao-volume");
            d["volumeProperty"] = MpvString("volume");
        }
        finally
        {
            _player?.SetPropertyAsync("mute", "yes");
            _vm.IsMuted = true;
            await bot.StopLocalAudioForTests();
            _vm.PropertyChanged -= OnPlayerChangedForDiscord;
            _discord = previous;
            _settings.DiscordFollowPlayerVolume = followBefore;
            _settings.DiscordVolume = discordBefore;
            _vm.Volume = 100;
            window.Close();
        }

        return d;
    }
}
