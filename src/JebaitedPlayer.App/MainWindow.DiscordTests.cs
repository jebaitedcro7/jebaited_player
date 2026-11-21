using System.Diagnostics;
using System.Text;
using Concentus;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services.Discord;

namespace JebaitedPlayer;

/// <summary>
/// V4-B u pravom playeru, bez stvarnog glasovnog kanala: --diag-ui-test discord (uz JEBAITED_DATA_DIR).
/// Hvata se zvuk samog playera (process loopback), kodira u Opus i broji umjesto slanja; provjeravaju se stvarna razina
/// (vrlo tih ton, dekodiran natrag), pravila tišine (pauza, video, otvoren YouTube) i prijava s izmišljenim tokenima
/// na stvarnom Discordu (mora biti BadToken).
/// </summary>
public partial class MainWindow
{
    private static readonly byte[] DiagOpusSilence = [0xF8, 0xFF, 0xFE];

    private async Task<Dictionary<string, object?>> DiscordUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 } dataDir)
        {
            r["error"] = "JEBAITED_DATA_DIR is required (never your real data)";
            return r;
        }

        r["supported"] = DiscordBot.IsSupported;
        r["build"] = Environment.OSVersion.Version.Build;
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 10000);
        _player?.SetPropertyAsync("mute", "yes");
        string music = _vm.CurrentPath!;
        r["inputsMusic"] = CurrentDiscordInputs().ToString();
        if (!DiscordBot.IsSupported)
        {
            return r;
        }

        int packets = 0, silences = 0;
        var collected = new List<byte[]>();
        bool collect = false;
        using var bot = new DiscordBot(dataDir, persist: false);
        bot.PacketSink = p =>
        {
            if (p.Span.SequenceEqual(DiagOpusSilence))
            {
                Interlocked.Increment(ref packets);
                Interlocked.Increment(ref silences);
                return;
            }

            Interlocked.Increment(ref packets);
            if (Volatile.Read(ref collect))
            {
                lock (collected)
                {
                    collected.Add(p.ToArray());
                }
            }
        };

        // Prozor od ms milisekundi: odluka, Opus paketi (bez okvira tišine), okviri tišine i najveća uhvaćena razina.
        async Task<Dictionary<string, object?>> Measure(int ms)
        {
            int p0 = Volatile.Read(ref packets), s0 = Volatile.Read(ref silences), peak = 0;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                peak = Math.Max(peak, bot.LastPeak);
                await Task.Delay(20);
            }

            int silence = Volatile.Read(ref silences) - s0;
            return new Dictionary<string, object?>
            {
                ["decision"] = bot.Decision.ToString(),
                ["packets"] = Volatile.Read(ref packets) - p0 - silence,
                ["silenceFrames"] = silence,
                ["peak"] = peak,
                ["ms"] = sw.ElapsedMilliseconds,
            };
        }

        // Promjena stanja (kao što će je javljati MainWindow): bilježe se okviri tišine pri prijelazu, pa mjerenje.
        async Task Switch(string name)
        {
            int s0 = Volatile.Read(ref silences);
            bot.Inputs = CurrentDiscordInputs();
            await Task.Delay(400);
            Dictionary<string, object?> m = await Measure(1000);
            m["transitionSilence"] = Volatile.Read(ref silences) - s0 - (int)m["silenceFrames"]!;
            m["inputs"] = bot.Inputs.ToString();
            r[name] = m;
        }

        bot.Inputs = CurrentDiscordInputs();
        bot.StartLocalAudioForTests();
        string? volume = MpvString("volume");
        try
        {
            // 1) Glazba utišana u mpv-u: hvatanje i kodiranje rade, razina je tišina.
            await Task.Delay(300);
            r["musicMuted"] = await Measure(1500);

            // 2) Utišana sesija (ao-mute=yes) uz mpv mute=no: ništa se ne čuje, a provjerava se je li i hvatanje tiho.
            //    mute=no samo ako mpv potvrdi ao-mute.
            _player?.SetPropertyAsync("ao-mute", "yes");
            await Task.Delay(300);
            string? aoMute = MpvString("ao-mute");
            r["aoMute"] = aoMute;
            if (aoMute == "yes")
            {
                _player?.SetPropertyAsync("volume", "2");
                _player?.SetPropertyAsync("mute", "no");
                await Task.Delay(300);
                r["musicAoMuted"] = await Measure(1500);
                _player?.SetPropertyAsync("mute", "yes");
            }

            _player?.SetPropertyAsync("ao-mute", "no");
            await Task.Delay(200);

            // 3) Stvarna razina, što tiše: glasnoća raste od 2 samo dok uhvaćeni vrh ne dosegne ~30 (oko -60 dBFS),
            //    pa se snima 1,5 s. Hvatanje je prije glavne glasnoće Windowsa, pa zvučnici dobivaju najviše toliko.
            //    Uhvaćeni Opus paketi dekodiraju se natrag: mora biti ton od 440 Hz.
            int[] steps = [2, 4, 6, 8, 10, 13, 16, 20];
            var ramp = new List<string>();
            int chosen = steps[^1];
            _player?.SetPropertyAsync("volume", steps[0].ToString(System.Globalization.CultureInfo.InvariantCulture));
            _player?.SetPropertyAsync("mute", "no");
            foreach (int step in steps)
            {
                _player?.SetPropertyAsync("volume", step.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await Task.Delay(150);
                int peak = (int)(await Measure(300))["peak"]!;
                ramp.Add($"{step}:{peak}");
                if (peak >= 30)
                {
                    chosen = step;
                    break;
                }
            }

            r["quietRamp"] = string.Join(" ", ramp);
            r["quietVolume"] = chosen;
            // Glasnoća zvučne sesije playera u Windowsima (Volume Mixer; mijenjaju je i drugi programi, npr. Discordovo
            // utišavanje). Hvatanje je nakon nje, pa objašnjava nižu razinu kad nije 100.
            r["sessionVolume"] = MpvString("ao-volume");

            // Usporedno i sirovi zvuk (drugi, neovisni process loopback), da se vidi nastaje li greška u hvatanju ili u kodiranju.
            var raw = new List<short>();
            using (var rawCapture = new ProcessAudioCapture(s =>
            {
                lock (raw)
                {
                    raw.AddRange(s.ToArray());
                }
            }))
            {
                rawCapture.Start();
                Volatile.Write(ref collect, true);
                Dictionary<string, object?> quiet = await Measure(1500);
                Volatile.Write(ref collect, false);
                rawCapture.Stop();
                _player?.SetPropertyAsync("mute", "yes");
                lock (collected)
                {
                    quiet["decoded"] = DecodeTone(collected, System.IO.Path.Combine(dataDir, "quiet-decoded.wav"));
                }

                lock (raw)
                {
                    quiet["raw"] = ToneStats(raw, Math.Min(OpusFramer.FrameSamples * 10, raw.Count));
                    WriteWav(System.IO.Path.Combine(dataDir, "quiet-raw.wav"), raw);
                }

                r["quiet"] = quiet;
            }

            // 4) Pauza: tišina, a nastavak opet šalje.
            _vm.SetPaused(true);
            await WaitForAsync(() => _vm.IsPaused, 3000);
            await Switch("paused");
            _vm.SetPaused(false);
            await WaitForAsync(() => !_vm.IsPaused, 3000);
            await Switch("resumed");

            // 5) Video: tišina (filmovi i spotovi se nikad ne šalju).
            string video = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(music)!, "fmt_webm_vp9_opus.webm");
            OpenFiles([video]);
            await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.CurrentPath == video && _vm.Position > 0.5, 10000);
            _player?.SetPropertyAsync("mute", "yes");
            await Switch("video");

            OpenFiles([music]);
            await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.CurrentPath == music && _vm.Position > 0.5, 10000);
            _player?.SetPropertyAsync("mute", "yes");
            await Switch("musicAgain");

            // 6) Otvoren prozor YouTube Musica: tišina dok je otvoren, i kad lokalna glazba svira.
            ShowYouTube(this);
            Views.YouTubeWindow w = _youTubeWindow!;
            w.Mute = true;
            await WaitForAsync(() => w.IsReady, 15000);
            await Switch("youTubeOpen");
            w.Close();
            await WaitForAsync(() => _youTubeWindow is null, 3000);
            if (_vm.IsPaused)
            {
                _vm.SetPaused(false);
                await WaitForAsync(() => !_vm.IsPaused, 3000);
            }

            await Switch("youTubeClosed");
        }
        finally
        {
            Volatile.Write(ref collect, false);
            _player?.SetPropertyAsync("mute", "yes");
            _player?.SetPropertyAsync("ao-mute", "no");
            if (volume is not null)
            {
                _player?.SetPropertyAsync("volume", volume);
            }

            await bot.StopLocalAudioForTests();
        }

        r["packetsTotal"] = packets;
        r["silenceTotal"] = silences;
        r["sentTotal"] = bot.PacketsSent;

        // 7) Izmišljeni tokeni na stvarnom Discordu (bez gatewaya i bez spremanja): oba moraju biti BadToken.
        //    Prvi je oblika pravog tokena (base64 izmišljenog ID-a), drugi ima neispravan prvi dio (NetCord ga odbija sam).
        string fakeId = Convert.ToBase64String(Encoding.ASCII.GetBytes("123456789012345678"));
        r["badToken"] = await TryToken(dataDir, fakeId + ".AAAAAA." + new string('x', 38));
        r["malformedToken"] = await TryToken(dataDir, new string('x', 26) + "." + new string('y', 6) + "." + new string('z', 38));
        return r;
    }

    /// <summary>
    /// V4-B na stvarnom Discordu (vlasnikov bot i server): --diag-ui-test discordlive, uz JEBAITED_DATA_DIR i
    /// JEBAITED_DISCORD_TOKEN_FILE. Bot ulazi u glasovni kanal u kojem je vlasnik, pa redom: ton, pjesma, pauza, nastavak,
    /// video (mora biti tišina), opet ton, izlazak. Zvuk se čuje i lokalno (hvatanje je nakon glasnoće playera).
    /// Token se čita iz datoteke i nikad ne ide u rezultat ni u dnevnik.
    /// </summary>
    private async Task<Dictionary<string, object?>> DiscordLiveUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 } dataDir)
        {
            r["error"] = "JEBAITED_DATA_DIR is required (never your real data)";
            return r;
        }

        if (Environment.GetEnvironmentVariable("JEBAITED_DISCORD_TOKEN_FILE") is not { Length: > 0 } tokenFile || !System.IO.File.Exists(tokenFile))
        {
            r["error"] = "JEBAITED_DISCORD_TOKEN_FILE is missing";
            return r;
        }

        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 10000);
        _player?.SetPropertyAsync("mute", "yes");
        string tone = _vm.CurrentPath!;
        string folder = System.IO.Path.GetDirectoryName(tone)!;
        string music = Environment.GetEnvironmentVariable("JEBAITED_DISCORD_MUSIC") is { Length: > 0 } m && System.IO.File.Exists(m)
            ? m
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, "..", "ciklus2", "album", "01 Prva.mp3"));
        string video = System.IO.Path.Combine(folder, "fmt_webm_vp9_opus.webm");

        var log = new List<string>();
        var timeline = new List<string>();
        var clock = Stopwatch.StartNew();
        void Mark(string what) => timeline.Add($"{clock.Elapsed.TotalSeconds:F1} s: {what}");

        using var bot = new DiscordBot(dataDir, persist: false)
        {
            NetCordLogLevel = NetCord.Logging.LogLevel.Information,
            LogSink = line =>
            {
                lock (log)
                {
                    log.Add(line);
                }
            },
        };
        string token = System.IO.File.ReadAllText(tokenFile).Trim();
        r["tokenLooksRight"] = DiscordBot.LooksLikeToken(token);
        bot.SetToken(token);

        bool connected = await bot.ConnectAsync();
        r["connect"] = new Dictionary<string, object?>
        {
            ["connected"] = connected, ["state"] = bot.State.ToString(), ["error"] = bot.Error.ToString(),
            ["detail"] = bot.ErrorDetail, ["botName"] = bot.BotName, ["ms"] = clock.ElapsedMilliseconds,
        };
        if (!connected)
        {
            r["log"] = log;
            return r;
        }

        Mark("bot logged in");
        await WaitForAsync(() => bot.ChannelWithMember() is not null, 10000);
        DiscordVoiceChannel? target = bot.ChannelWithMember();
        r["voiceChannels"] = bot.VoiceChannels().Count;
        r["channel"] = target is null ? null : $"{target.GuildName} / {target.ChannelName}";
        r["inviteUrlHasOnlyConnectSpeak"] = bot.InviteUrl?.EndsWith("permissions=" + DiscordBot.Permissions, StringComparison.Ordinal);
        r["ownerKnown"] = bot.OwnerUserId != 0;
        r["ownerChannel"] = bot.OwnerVoiceChannel() is { } own ? $"{own.GuildName} / {own.ChannelName}" : null;
        if (target is not null)
        {
            r["botPermissions"] = bot.BotChannelPermissions(target.GuildId, target.ChannelId)?.ToString();
            r["joinProblem"] = bot.JoinProblem(target.GuildId, target.ChannelId)?.ToString();
        }

        if (target is null)
        {
            r["error"] = "nobody is in a voice channel the bot can see";
            await bot.DisconnectAsync();
            r["log"] = log;
            return r;
        }

        // Zvuk mora stvarno svirati (hvatanje je nakon glasnoće playera); 80 % glasnoće playera.
        void Audible()
        {
            _player?.SetPropertyAsync("volume", "80");
            _player?.SetPropertyAsync("mute", "no");
        }

        async Task Open(string path)
        {
            OpenFiles([path]);
            await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.CurrentPath == path && _vm.Position > 0.3, 10000);
            Audible();
        }

        async Task Segment(string name, int seconds)
        {
            bot.Inputs = CurrentDiscordInputs();
            long p0 = bot.PacketsSent;
            int peak = 0;
            Mark($"{name} ({seconds} s)");
            var s = Stopwatch.StartNew();
            while (s.Elapsed.TotalSeconds < seconds)
            {
                bot.Inputs = CurrentDiscordInputs();
                peak = Math.Max(peak, bot.LastPeak);
                await Task.Delay(50);
            }

            r[name] = new Dictionary<string, object?>
            {
                ["decision"] = bot.Decision.ToString(), ["packetsSent"] = bot.PacketsSent - p0, ["peak"] = peak,
                ["state"] = bot.State.ToString(), ["error"] = bot.Error.ToString(), ["file"] = System.IO.Path.GetFileName(_vm.CurrentPath),
            };
        }

        Audible();
        bot.Inputs = CurrentDiscordInputs();
        bool joined = await bot.JoinAsync(target.GuildId, target.ChannelId);
        r["join"] = new Dictionary<string, object?>
        {
            ["joined"] = joined, ["state"] = bot.State.ToString(), ["error"] = bot.Error.ToString(), ["detail"] = bot.ErrorDetail,
        };
        Mark(joined ? "bot joined the channel" : "join failed");
        try
        {
            if (joined)
            {
                await Segment("tone", 10);
                await Open(music);
                await Segment("music", 15);
                _vm.SetPaused(true);
                await WaitForAsync(() => _vm.IsPaused, 3000);
                await Segment("paused", 5);
                _vm.SetPaused(false);
                await WaitForAsync(() => !_vm.IsPaused, 3000);
                await Segment("resumed", 5);
                await Open(video);
                await Segment("video", 5);
                await Open(tone);
                await Segment("toneAgain", 5);
            }
        }
        finally
        {
            _player?.SetPropertyAsync("mute", "yes");
            Mark("leaving");
            await bot.LeaveAsync();
            await bot.DisconnectAsync();
            r["after"] = new Dictionary<string, object?> { ["state"] = bot.State.ToString(), ["error"] = bot.Error.ToString(), ["sent"] = bot.PacketsSent };
        }

        r["timeline"] = timeline;
        lock (log)
        {
            r["log"] = log.TakeLast(300).ToList();
        }

        return r;
    }

    private static Dictionary<string, object?> DiscordPanels(Views.DiscordWindow w) => new()
    {
        ["setup"] = w.SetupPanel.IsVisible,
        ["bot"] = w.BotPanel.IsVisible,
        ["channel"] = w.ChannelPanel.IsVisible,
        ["connect"] = w.ConnectButton.IsVisible,
        ["disconnect"] = w.DisconnectButton.IsVisible,
        ["botText"] = w.BotText.Text,
        ["status"] = w.StatusText.Text,
    };

    /// <summary>
    /// V4-B sučelje bez pravog bota: --diag-ui-test discordui (uz JEBAITED_DATA_DIR). Prečac, loš oblik tokena, izmišljeni
    /// token (stvarni Discord ga odbija), spremanje kriptirano, promjena i uklanjanje tokena, gumb i izbornik u mini playeru.
    /// </summary>
    private async Task<Dictionary<string, object?>> DiscordWindowUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 })
        {
            r["error"] = "JEBAITED_DATA_DIR is required (never your real data)";
            return r;
        }

        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 10000);
        _player?.SetPropertyAsync("mute", "yes");
        string tokenFile = System.IO.Path.Combine(AppPaths.DataDir, "discord-bot.dat");

        // 1) Prečac Ctrl+Shift+D otvara prozor; bez tokena je vidljivo samo postavljanje.
        HandleShortcut(System.Windows.Input.Key.D, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift);
        await WaitForAsync(() => _discordWindow is { IsLoaded: true }, 5000);
        Views.DiscordWindow w = _discordWindow!;
        await Task.Delay(200);
        r["start"] = DiscordPanels(w);
        r["setupScreenshot"] = SaveWindowPng("discord_setup", w);

        // 2) Nešto što nije token: poruka, ništa se ne sprema.
        await w.SaveTokenAsync("this is not a token");
        r["badShape"] = DiscordPanels(w);
        r["badShapeFile"] = System.IO.File.Exists(tokenFile);

        // 3) Izmišljeni token pravog oblika: sprema se kriptiran, odmah se spaja, Discord ga odbija.
        string fake = Convert.ToBase64String(Encoding.ASCII.GetBytes("123456789012345678")) + ".AAAAAA." + new string('x', 38);
        await w.SaveTokenAsync(fake);
        await WaitForAsync(() => Discord.State is DiscordBotState.Error or DiscordBotState.Online, 15000);
        await Task.Delay(200);
        r["fake"] = DiscordPanels(w);
        r["fakeState"] = $"{Discord.State}/{Discord.Error}";
        r["fakeFile"] = System.IO.File.Exists(tokenFile);
        r["fakePlain"] = System.IO.File.Exists(tokenFile) && System.IO.File.ReadAllText(tokenFile).Contains(fake, StringComparison.Ordinal);
        r["fakeScreenshot"] = SaveWindowPng("discord_badtoken", w);

        // 4) Promjena tokena vraća postavljanje; prazno polje uklanja token i datoteku.
        ClickButton(w.ChangeTokenButton);
        await Task.Delay(150);
        r["changeShowsSetup"] = w.SetupPanel.IsVisible;
        await w.SaveTokenAsync(string.Empty);
        r["removed"] = DiscordPanels(w);
        r["removedFile"] = System.IO.File.Exists(tokenFile);
        r["removedHasToken"] = Discord.HasToken;

        // 5) Mini player: gumb otvara prozor (vlasnik je mini player), stavka u izborniku, opis gumba.
        w.Close();
        ShowMiniPlayer();
        await Task.Delay(600);
        Views.MiniPlayerWindow mini = _mini!;
        string menuText = Loc.T("menu.discord");
        r["miniButton"] = mini.DiscordButton.IsVisible;
        r["miniMenu"] = mini.ContextMenu?.Items.OfType<System.Windows.Controls.MenuItem>().Any(i => (i.Header as string)?.Replace("__", "_", StringComparison.Ordinal) == menuText);
        ClickButton(mini.DiscordButton);
        await WaitForAsync(() => _discordWindow is { IsLoaded: true }, 3000);
        r["miniOpens"] = _discordWindow is not null;
        r["miniOwner"] = _discordWindow?.Owner == mini;
        r["miniTooltip"] = mini.DiscordButton.ToolTip as string;
        r["miniLit"] = mini.DiscordButtonIsLit;
        r["miniScreenshot"] = SaveWindowPng("mini_discord", mini);
        _discordWindow?.Close();

        // 6) Zatvaranje playera bez spojenog bota ne čeka.
        var sw = Stopwatch.StartNew();
        await CloseDiscordAsync();
        r["closeMs"] = sw.ElapsedMilliseconds;
        return r;
    }

    /// <summary>
    /// V4-B cijeli put kroz sučelje na stvarnom Discordu: --diag-ui-test discorduilive (uz JEBAITED_DATA_DIR i
    /// JEBAITED_DISCORD_TOKEN_FILE; vlasnik je u glasovnom kanalu). Token se upisuje u prozor, „Uđi u moj kanal”,
    /// glazba (gumb u mini playeru svijetli), video (tišina), glazba, „Izađi”, odjava pri zatvaranju; token se na kraju uklanja.
    /// </summary>
    private async Task<Dictionary<string, object?>> DiscordWindowLiveUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 }
            || Environment.GetEnvironmentVariable("JEBAITED_DISCORD_TOKEN_FILE") is not { Length: > 0 } tokenFile || !System.IO.File.Exists(tokenFile))
        {
            r["error"] = "JEBAITED_DATA_DIR and JEBAITED_DISCORD_TOKEN_FILE are required";
            return r;
        }

        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 10000);
        _player?.SetPropertyAsync("mute", "yes");
        string tone = _vm.CurrentPath!;
        string folder = System.IO.Path.GetDirectoryName(tone)!;
        string music = Environment.GetEnvironmentVariable("JEBAITED_DISCORD_MUSIC") is { Length: > 0 } m && System.IO.File.Exists(m)
            ? m
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, "..", "ciklus2", "album", "01 Prva.mp3"));
        string video = System.IO.Path.Combine(folder, "fmt_webm_vp9_opus.webm");

        void Audible()
        {
            _player?.SetPropertyAsync("volume", "80");
            _player?.SetPropertyAsync("mute", "no");
        }

        async Task Open(string path)
        {
            OpenFiles([path]);
            await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.CurrentPath == path && _vm.Position > 0.3, 10000);
            Audible();
        }

        ShowMiniPlayer();
        await Task.Delay(600);
        Views.MiniPlayerWindow mini = _mini!;
        ShowDiscord(mini);
        await WaitForAsync(() => _discordWindow is { IsLoaded: true }, 5000);
        Views.DiscordWindow w = _discordWindow!;

        Dictionary<string, object?> Live() => new()
        {
            ["state"] = $"{Discord.State}/{Discord.Error}",
            ["decision"] = Discord.Decision.ToString(),
            ["channelText"] = w.ChannelText.Text,
            ["sendingText"] = w.SendingText.Text,
            ["level"] = (int)w.LevelBar.Value,
            ["miniLit"] = mini.DiscordButtonIsLit,
            ["miniTooltip"] = mini.DiscordButton.ToolTip as string,
            ["status"] = w.StatusText.Text,
            ["sent"] = Discord.PacketsSent,
        };

        // 1) Token kroz prozor (kao korisnik): sprema se i bot se odmah spaja.
        await w.SaveTokenAsync(System.IO.File.ReadAllText(tokenFile));
        await WaitForAsync(() => Discord.State == DiscordBotState.Online && Discord.ChannelWithMember() is not null, 15000);
        await Task.Delay(400);
        r["connected"] = DiscordPanels(w);
        r["connectedState"] = $"{Discord.State}/{Discord.Error}";
        r["channelList"] = w.Channels.Count;
        if (Discord.State != DiscordBotState.Online)
        {
            await CloseDiscordAsync();
            Discord.SetToken(null);
            return r;
        }

        // 2) „Uđi u moj kanal” i glazba (čuje se i lokalno).
        Audible();
        UpdateDiscordInputs();
        r["joined"] = await w.JoinMineAsync();
        await Task.Delay(500);
        r["joinedScreenshot"] = SaveWindowPng("discord_inchannel", w);
        await Open(music);
        await Task.Delay(6000);
        mini.Tick();
        r["music"] = Live();
        r["miniScreenshot"] = SaveWindowPng("mini_discord_live", mini);

        // 3) Video: tišina, gumb ne svijetli.
        await Open(video);
        await Task.Delay(3000);
        mini.Tick();
        r["video"] = Live();

        // 4) Opet glazba.
        await Open(music);
        await Task.Delay(4000);
        mini.Tick();
        r["musicAgain"] = Live();
        _player?.SetPropertyAsync("mute", "yes");

        // 5) „Izađi iz kanala”, pa zatvaranje (bot se odjavi), pa uklanjanje tokena iz testne mape.
        ClickButton(w.LeaveButton);
        await WaitForAsync(() => Discord.State == DiscordBotState.Online, 5000);
        mini.Tick();
        r["left"] = Live();
        var sw = Stopwatch.StartNew();
        await CloseDiscordAsync();
        r["closed"] = new Dictionary<string, object?> { ["state"] = Discord.State.ToString(), ["active"] = Discord.IsActive, ["ms"] = sw.ElapsedMilliseconds };
        Discord.SetToken(null);
        r["tokenFileAfter"] = System.IO.File.Exists(System.IO.Path.Combine(AppPaths.DataDir, "discord-bot.dat"));
        return r;
    }

    /// <summary>Dekodira uhvaćene Opus pakete (bez prvih 200 ms), po želji sprema WAV, i daje razinu i frekvenciju.</summary>
    private static Dictionary<string, object?> DecodeTone(List<byte[]> packets, string? wavPath = null)
    {
        IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(ProcessAudioCapture.SampleRate, ProcessAudioCapture.Channels);
        var buffer = new short[OpusFramer.FrameSamples];
        var pcm = new List<short>();
        foreach (byte[] packet in packets)
        {
            int n = decoder.Decode(packet, buffer, OpusFramer.FrameSamplesPerChannel, false);
            pcm.AddRange(buffer.AsSpan(0, n * ProcessAudioCapture.Channels).ToArray());
        }

        if (wavPath is not null)
        {
            WriteWav(wavPath, pcm);
        }

        Dictionary<string, object?> stats = ToneStats(pcm, Math.Min(OpusFramer.FrameSamples * 10, pcm.Count));
        stats["packets"] = packets.Count;
        return stats;
    }

    /// <summary>Lijevi kanal od uzorka from: vrh, RMS, frekvencija (prolasci s histerezom) i najveći skok između uzoraka.</summary>
    private static Dictionary<string, object?> ToneStats(List<short> interleaved, int from)
    {
        var left = new List<short>();
        for (int i = from - (from % 2); i < interleaved.Count; i += 2)
        {
            left.Add(interleaved[i]);
        }

        int peak = left.Count == 0 ? 0 : left.Max(s => Math.Abs((int)s));
        double rms = left.Count == 0 ? 0 : Math.Sqrt(left.Average(s => (double)s * s));
        int threshold = Math.Max((int)(rms * 0.7), 2), rising = 0, maxStep = 0;
        bool high = false;
        for (int i = 0; i < left.Count; i++)
        {
            short s = left[i];
            if (i > 0)
            {
                maxStep = Math.Max(maxStep, Math.Abs(s - left[i - 1]));
            }

            if (!high && s > threshold)
            {
                high = true;
                rising++;
            }
            else if (high && s < -threshold)
            {
                high = false;
            }
        }

        double seconds = left.Count / (double)ProcessAudioCapture.SampleRate;
        return new Dictionary<string, object?>
        {
            ["seconds"] = Math.Round(seconds, 2),
            ["peak"] = peak,
            ["rms"] = Math.Round(rms, 1),
            ["maxStep"] = maxStep,
            ["hz"] = seconds > 0 ? Math.Round(rising / seconds, 1) : 0,
        };
    }

    private static void WriteWav(string path, List<short> interleaved)
    {
        using var w = new System.IO.BinaryWriter(System.IO.File.Create(path));
        int bytes = interleaved.Count * 2;
        w.Write("RIFF"u8);
        w.Write(36 + bytes);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)ProcessAudioCapture.Channels);
        w.Write(ProcessAudioCapture.SampleRate);
        w.Write(ProcessAudioCapture.SampleRate * ProcessAudioCapture.Channels * 2);
        w.Write((short)(ProcessAudioCapture.Channels * 2));
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(bytes);
        foreach (short s in interleaved)
        {
            w.Write(s);
        }
    }

    private static async Task<Dictionary<string, object?>> TryToken(string dataDir, string token)
    {
        using var bot = new DiscordBot(dataDir, persist: false);
        bot.SetToken(token);
        var sw = Stopwatch.StartNew();
        bool ok = await bot.ConnectAsync();
        return new Dictionary<string, object?>
        {
            ["looksLikeToken"] = DiscordBot.LooksLikeToken(token),
            ["connected"] = ok,
            ["state"] = bot.State.ToString(),
            ["error"] = bot.Error.ToString(),
            ["detail"] = bot.ErrorDetail,
            ["ms"] = sw.ElapsedMilliseconds,
            ["file"] = System.IO.File.Exists(System.IO.Path.Combine(dataDir, "discord-bot.dat")),
        };
    }
}
