using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using Concentus;
using JebaitedPlayer.Services.Discord;

namespace JebaitedPlayer.App.Tests;

/// <summary>
/// V4-B bot za Discord, bez spajanja na Discord: pravila slanja zvuka, Opus okviri, oblik i spremanje tokena,
/// te libdave.dll (izvozi koje NetCord treba i verzija DAVE protokola).
/// </summary>
[TestClass]
public sealed class DiscordBotTests
{
    private const int Rate = ProcessAudioCapture.SampleRate;

    // ---------- DiscordAudioGate: samo lokalna glazba i radio ----------

    private static DiscordAudioDecision Decide(bool media, bool paused, bool video, bool youTube, bool spotify) =>
        DiscordAudioGate.Decide(new DiscordAudioInputs(media, paused, video, youTube, spotify));

    [TestMethod]
    public void Gate_sends_only_unpaused_local_music_or_radio()
    {
        // Svih 32 kombinacija: šalje se točno jedna (ima medij, ne pauzira, bez slike, bez YouTubea i Spotifyja).
        int sends = 0;
        for (int bits = 0; bits < 32; bits++)
        {
            var s = new DiscordAudioInputs(
                HasMedia: (bits & 1) != 0, IsPaused: (bits & 2) != 0, HasVideo: (bits & 4) != 0,
                YouTubeOpen: (bits & 8) != 0, SpotifyActive: (bits & 16) != 0);
            DiscordAudioDecision d = DiscordAudioGate.Decide(s);
            bool expected = s.HasMedia && !s.IsPaused && !s.HasVideo && !s.YouTubeOpen && !s.SpotifyActive;
            Assert.AreEqual(expected, d == DiscordAudioDecision.Send, $"{s} -> {d}");
            sends += d == DiscordAudioDecision.Send ? 1 : 0;
        }

        Assert.AreEqual(1, sends);
    }

    [TestMethod]
    public void Gate_names_the_reason_for_silence_in_a_fixed_order()
    {
        // Redoslijed: YouTube > Spotify > ništa ne svira > slika > pauza.
        Assert.AreEqual(DiscordAudioDecision.SilenceYouTube, Decide(media: true, paused: false, video: true, youTube: true, spotify: true));
        Assert.AreEqual(DiscordAudioDecision.SilenceSpotify, Decide(media: true, paused: false, video: true, youTube: false, spotify: true));
        Assert.AreEqual(DiscordAudioDecision.SilenceNothingPlaying, Decide(media: false, paused: false, video: true, youTube: false, spotify: false));
        Assert.AreEqual(DiscordAudioDecision.SilenceVideo, Decide(media: true, paused: true, video: true, youTube: false, spotify: false));
        Assert.AreEqual(DiscordAudioDecision.SilencePaused, Decide(media: true, paused: true, video: false, youTube: false, spotify: false));

        // Spotify je izvor: i kad je u playeru otvorena glazba, ništa se ne šalje.
        Assert.AreEqual(DiscordAudioDecision.SilenceSpotify, Decide(media: true, paused: false, video: false, youTube: false, spotify: true));

        // Početno stanje (ništa postavljeno) je tišina, kao i zadana odluka bota.
        Assert.AreEqual(DiscordAudioDecision.SilenceNothingPlaying, DiscordAudioGate.Decide(default));
    }

    // ---------- OpusFramer: okviri od 20 ms ----------

    private static short[] Tone(int samplesPerChannel, double hz, double amplitude = 0.5)
    {
        var pcm = new short[samplesPerChannel * ProcessAudioCapture.Channels];
        for (int i = 0; i < samplesPerChannel; i++)
        {
            short v = (short)(Math.Sin(2 * Math.PI * hz * i / Rate) * amplitude * short.MaxValue);
            pcm[2 * i] = v;
            pcm[(2 * i) + 1] = v;
        }

        return pcm;
    }

    [TestMethod]
    public void Framer_makes_one_packet_per_full_20_ms_even_from_uneven_chunks()
    {
        Assert.AreEqual(960, OpusFramer.FrameSamplesPerChannel);
        Assert.AreEqual(1920, OpusFramer.FrameSamples);

        var packets = new List<byte[]>();
        var framer = new OpusFramer(p => packets.Add(p.ToArray()));
        short[] pcm = Tone((OpusFramer.FrameSamplesPerChannel * 3) + 100, 440);

        // Nepravilni komadi, kao iz WASAPI-ja (i neparni, usred para lijevo/desno).
        int[] chunks = [7, 333, 1000, 1, 2500];
        int pos = 0;
        for (int k = 0; pos < pcm.Length; k++)
        {
            int n = Math.Min(chunks[k % chunks.Length], pcm.Length - pos);
            framer.Push(pcm.AsSpan(pos, n));
            pos += n;
        }

        Assert.AreEqual(3, packets.Count, "3 full frames; the last 100 samples wait for the next frame");
        Assert.AreEqual(3, framer.PacketsEncoded);
        Assert.IsTrue(packets.All(p => p.Length is > 0 and < 4000));
    }

    [TestMethod]
    public void Reset_drops_the_started_frame()
    {
        var packets = new List<byte[]>();
        var framer = new OpusFramer(p => packets.Add(p.ToArray()));
        short[] frame = Tone(OpusFramer.FrameSamplesPerChannel, 440);
        int half = frame.Length / 2;

        framer.Push(frame.AsSpan(0, half));
        framer.Reset();
        framer.Push(frame.AsSpan(0, half));
        Assert.AreEqual(0, packets.Count, "after Reset, half a frame plus half a frame is still not a frame");

        framer.Push(frame.AsSpan(half));
        Assert.AreEqual(1, packets.Count);
    }

    [TestMethod]
    public void Each_packet_is_its_own_copy()
    {
        // Paketi čekaju u redu prije slanja, pa ih sljedeći okvir ne smije prepisati.
        var packets = new List<ReadOnlyMemory<byte>>();
        var framer = new OpusFramer(packets.Add);
        framer.Push(Tone(OpusFramer.FrameSamplesPerChannel, 440, 0.3));
        byte[] first = packets[0].ToArray();

        framer.Push(Tone(OpusFramer.FrameSamplesPerChannel * 4, 3000, 0.9));

        Assert.AreEqual(5, packets.Count);
        CollectionAssert.AreEqual(first, packets[0].ToArray());
    }

    private static double Rms(IReadOnlyList<short> s, int from)
    {
        double sum = 0;
        for (int i = from; i < s.Count; i++)
        {
            sum += (double)s[i] * s[i];
        }

        return Math.Sqrt(sum / (s.Count - from));
    }

    private static int LeftZeroCrossings(IReadOnlyList<short> s, int from)
    {
        int z = 0;
        for (int i = from + 2; i < s.Count; i += 2)
        {
            z += (s[i - 2] < 0) != (s[i] < 0) ? 1 : 0;
        }

        return z;
    }

    [TestMethod]
    public void Encoded_music_decodes_back_to_the_same_tone()
    {
        short[] input = Tone(Rate, 440); // 1 s
        var packets = new List<byte[]>();
        var framer = new OpusFramer(p => packets.Add(p.ToArray()));
        framer.Push(input);
        Assert.AreEqual(50, packets.Count, "1 s = 50 frames of 20 ms");

        int bytes = packets.Sum(p => p.Length);
        Assert.IsTrue(bytes is > 2_000 and < 25_000, $"1 s of Opus at ~96 kbit/s (VBR) took {bytes} B");

        IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(Rate, ProcessAudioCapture.Channels);
        var output = new List<short>(input.Length);
        var buffer = new short[OpusFramer.FrameSamples];
        foreach (byte[] packet in packets)
        {
            int n = decoder.Decode(packet, buffer, OpusFramer.FrameSamplesPerChannel, false);
            Assert.AreEqual(OpusFramer.FrameSamplesPerChannel, n);
            output.AddRange(buffer);
        }

        // Preskače se prvih 100 ms (zagrijavanje kodera i kašnjenje).
        int from = OpusFramer.FrameSamples * 5;
        double rmsIn = Rms(input, from), rmsOut = Rms(output, from);
        Assert.IsTrue(Math.Abs(rmsOut - rmsIn) / rmsIn < 0.1, $"RMS in {rmsIn:F0}, out {rmsOut:F0}");

        int zIn = LeftZeroCrossings(input, from), zOut = LeftZeroCrossings(output, from);
        Assert.IsTrue(Math.Abs(zOut - zIn) <= zIn * 0.02, $"zero crossings in {zIn}, out {zOut} (440 Hz)");
    }

    [TestMethod]
    public void Silence_frame_sent_on_pause_is_valid_opus_silence()
    {
        FieldInfo field = typeof(DiscordBot).GetField("OpusSilence", BindingFlags.NonPublic | BindingFlags.Static)!;
        byte[] silence = (byte[])field.GetValue(null)!;
        CollectionAssert.AreEqual(new byte[] { 0xF8, 0xFF, 0xFE }, silence);

        IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(Rate, ProcessAudioCapture.Channels);
        var buffer = new short[OpusFramer.FrameSamples];
        int n = decoder.Decode(silence, buffer, OpusFramer.FrameSamplesPerChannel, false);
        Assert.AreEqual(OpusFramer.FrameSamplesPerChannel, n, "one 20 ms frame");
        Assert.IsTrue(buffer.All(v => Math.Abs((int)v) <= 1), "decodes to silence");
    }

    // ---------- Token ----------

    // Izmišljen token istog oblika kao pravi: base64 izmišljenog ID-a bota (samo znamenke) i još dva dijela.
    // Sastavlja se u kodu, nikad pravi token. ID od 18 znamenki daje prvi dio od 24 znaka, pa je duljina 32 + c.
    private static string BotIdPart(string id = "123456789012345678") =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes(id)).TrimEnd('=');

    private static string FakeToken(int c = 38, string id = "123456789012345678") =>
        BotIdPart(id) + "." + new string('y', 6) + "." + new string('z', c);

    [TestMethod]
    public void Token_shape_check_accepts_three_parts_and_rejects_common_mistakes()
    {
        Assert.IsTrue(DiscordBot.LooksLikeToken(FakeToken()));                         // 70 znakova, uobičajeno
        Assert.IsTrue(DiscordBot.LooksLikeToken(FakeToken(18)));                       // 50, donja granica
        Assert.IsTrue(DiscordBot.LooksLikeToken(FakeToken(68)));                       // 100, gornja granica
        Assert.IsTrue(DiscordBot.LooksLikeToken(FakeToken(id: "1234567890123456789"))); // ID od 19 znamenki (bez „=”)
        Assert.IsTrue(DiscordBot.LooksLikeToken(BotIdPart() + ".ab-_cd." + new string('z', 38)));  // base64url znakovi

        Assert.IsFalse(DiscordBot.LooksLikeToken(FakeToken(17)));  // 49
        Assert.IsFalse(DiscordBot.LooksLikeToken(FakeToken(69)));  // 101
        Assert.IsFalse(DiscordBot.LooksLikeToken(string.Empty));
        Assert.IsFalse(DiscordBot.LooksLikeToken("Bot " + FakeToken()));          // zalijepljen s „Bot ”
        Assert.IsFalse(DiscordBot.LooksLikeToken(FakeToken() + "\n"));            // novi red na kraju
        Assert.IsFalse(DiscordBot.LooksLikeToken(BotIdPart() + "." + new string('z', 40)));  // dva dijela
        Assert.IsFalse(DiscordBot.LooksLikeToken(FakeToken(30) + ".abc"));        // četiri dijela
        Assert.IsFalse(DiscordBot.LooksLikeToken(BotIdPart() + ".." + new string('z', 44)));  // prazan srednji dio

        // Prvi dio mora biti ID bota (base64 znamenki), kao što provjerava NetCord.
        Assert.IsFalse(DiscordBot.LooksLikeToken(new string('x', 24) + "." + new string('y', 6) + "." + new string('z', 38)));
        Assert.IsFalse(DiscordBot.LooksLikeToken(BotIdPart("abcdefghijklmnopqr") + ".yyyyyy." + new string('z', 38)));
        Assert.IsFalse(DiscordBot.LooksLikeToken(BotIdPart("012345678901234567") + ".yyyyyy." + new string('z', 38)));

        // Česte zabune iz Developer Portala: Client Secret (32 znaka) i Application ID (samo znamenke).
        Assert.IsFalse(DiscordBot.LooksLikeToken(new string('s', 32)));
        Assert.IsFalse(DiscordBot.LooksLikeToken("123456789012345678"));
    }

    [TestMethod]
    public void Token_is_trimmed_saved_encrypted_and_cleared_by_blank_input()
    {
        string dir = Path.Combine(Path.GetTempPath(), "jp-discord-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string token = FakeToken();
            string file = Path.Combine(dir, "discord-bot.dat");

            using (var bot = new DiscordBot(dir))
            {
                Assert.AreEqual(DiscordBotState.NoToken, bot.State);
                Assert.IsFalse(bot.HasToken);

                bot.SetToken("  " + token + "\n");
                Assert.IsTrue(bot.HasToken);
                Assert.AreEqual(DiscordBotState.Offline, bot.State);
            }

            Assert.IsTrue(File.Exists(file));
            string stored = File.ReadAllText(file);
            Assert.IsFalse(stored.Contains(token, StringComparison.Ordinal), "the token must not be stored as plain text");
            Assert.IsFalse(stored.Contains(new string('z', 20), StringComparison.Ordinal), "not even a part of it");

            using (var again = new DiscordBot(dir))
            {
                Assert.IsTrue(again.HasToken, "the token is read back after a restart");
                Assert.AreEqual(DiscordBotState.Offline, again.State);
                Assert.IsNull(again.InviteUrl, "no invite link before the bot has logged in");

                again.SetToken("   ");
                Assert.IsFalse(again.HasToken);
                Assert.AreEqual(DiscordBotState.NoToken, again.State);
            }

            Assert.IsFalse(File.Exists(file), "a blank token deletes the file");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [TestMethod]
    public async Task Token_that_is_not_a_bot_token_is_BadToken_without_network()
    {
        // NetCord ga odbija prije slanja (prvi dio nije ID bota): to je kriv token, a ne „Other”.
        using var bot = new DiscordBot(Path.GetTempPath(), persist: false);
        bot.SetToken(new string('x', 24) + "." + new string('y', 6) + "." + new string('z', 38));

        bool connected = await bot.ConnectAsync();

        Assert.IsFalse(connected);
        Assert.AreEqual(DiscordBotState.Error, bot.State);
        Assert.AreEqual(DiscordBotError.BadToken, bot.Error);
    }

    [TestMethod]
    public void Every_error_and_sending_state_has_a_text_in_all_six_languages()
    {
        try
        {
            foreach (string language in new[] { "en", "de", "hr", "bs", "sr", "sl" })
            {
                JebaitedPlayer.Localization.Loc.SetLanguage(language);
                foreach (DiscordBotError error in Enum.GetValues<DiscordBotError>())
                {
                    string? text = JebaitedPlayer.Views.DiscordWindow.ErrorText(error, "Connect");
                    if (error == DiscordBotError.None)
                    {
                        Assert.IsNull(text);
                        continue;
                    }

                    Assert.IsTrue(text is { Length: > 8 } && !text.StartsWith("discord.", StringComparison.Ordinal), $"{language} {error}: '{text}'");
                }

                var decisions = Enum.GetValues<DiscordAudioDecision>().Select(JebaitedPlayer.Views.DiscordWindow.DecisionText).ToList();
                Assert.IsTrue(decisions.All(t => t.Length > 5 && !t.StartsWith("discord.", StringComparison.Ordinal)), language);
                Assert.AreEqual(decisions.Count, decisions.Distinct().Count(), $"{language}: every sending state has its own text");
            }
        }
        finally
        {
            JebaitedPlayer.Localization.Loc.SetLanguage("hr");
        }
    }

    [TestMethod]
    public void Bot_asks_only_for_View_Channel_Connect_and_Speak()
    {
        // View Channel treba za privatne kanale (stvarni test na vlasnikovom serveru); ništa više od toga.
        Assert.AreEqual((1L << 10) | (1L << 20) | (1L << 21), DiscordBot.Permissions);
        Assert.AreEqual(3_146_752L, DiscordBot.Permissions);
    }

    // ---------- libdave.dll (OpenSSL 3 build) ----------

    private static string LibdavePath => Path.Combine(AppContext.BaseDirectory, "libdave.dll");

    private static void RequireLibdave()
    {
        if (!File.Exists(LibdavePath))
        {
            Assert.Inconclusive("libdave.dll is missing next to the tests: run tools\\get-libdave.ps1 and rebuild.");
        }
    }

    /// <summary>Što NetCord zove iz libdave: čita se iz tablice P/Invoke uvoza (ImplMap) u NetCord.dll.</summary>
    private static List<string> NetCordLibdaveImports()
    {
        using FileStream fs = File.OpenRead(typeof(NetCord.Gateway.GatewayClient).Assembly.Location);
        using var pe = new PEReader(fs);
        MetadataReader md = pe.GetMetadataReader();
        var names = new List<string>();
        foreach (MethodDefinitionHandle h in md.MethodDefinitions)
        {
            MethodDefinition m = md.GetMethodDefinition(h);
            MethodImport import = m.GetImport();
            if (import.Module.IsNil || md.GetString(md.GetModuleReference(import.Module).Name) != "libdave")
            {
                continue;
            }

            names.Add(import.Name.IsNil ? md.GetString(m.Name) : md.GetString(import.Name));
        }

        return names.Distinct().Order(StringComparer.Ordinal).ToList();
    }

    [TestMethod]
    public void Libdave_exports_every_function_NetCord_calls()
    {
        RequireLibdave();
        List<string> needed = NetCordLibdaveImports();
        Assert.AreEqual(30, needed.Count,
            "NetCord changed the set of libdave functions it calls; re-check third_party/libdave/SOURCE.md: " + string.Join(", ", needed));

        IntPtr lib = NativeLibrary.Load(LibdavePath);
        try
        {
            List<string> missing = needed.Where(n => !NativeLibrary.TryGetExport(lib, n, out _)).ToList();
            Assert.AreEqual(0, missing.Count, "missing in libdave.dll: " + string.Join(", ", missing));
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    /// <summary>NetCordovi P/Invoke pozivi prema libdave: ulaz → broj parametara (iz potpisa metode).</summary>
    private static Dictionary<string, int> NetCordLibdaveSignatures() =>
        typeof(NetCord.Gateway.GatewayClient).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Select(m => (Method: m, Import: m.GetCustomAttribute<DllImportAttribute>()))
            .Where(x => x.Import is { Value: "libdave" })
            .ToDictionary(x => x.Import!.EntryPoint ?? x.Method.Name, x => x.Method.GetParameters().Length);

    [TestMethod]
    public void Libdave_header_matches_every_NetCord_call_signature()
    {
        // Imena nisu dovoljna: libdave 1.3.0 ima ista imena, ali ProcessProposals/ProcessWelcome bez popisa sudionika,
        // pa je DAVE na pravom serveru padao. Zato se uspoređuje broj parametara s DAVE_EXPORT deklaracijama.
        string headerPath = Path.Combine(AppContext.BaseDirectory, "dave.h");
        Assert.IsTrue(File.Exists(headerPath), "dave.h is missing next to the tests");
        string header = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(headerPath), @"\s+", " ");
        var declared = System.Text.RegularExpressions.Regex.Matches(header, @"DAVE_EXPORT [^;()]*?\b(dave\w+)\s*\(([^)]*)\)")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim() is "" or "void" ? 0 : m.Groups[2].Value.Split(',').Length);

        Dictionary<string, int> netcord = NetCordLibdaveSignatures();
        Assert.AreEqual(30, netcord.Count);
        var problems = netcord
            .Where(kv => !declared.TryGetValue(kv.Key, out int n) || n != kv.Value)
            .Select(kv => $"{kv.Key}: NetCord {kv.Value}, dave.h {(declared.TryGetValue(kv.Key, out int n) ? n : -1)}")
            .ToList();
        Assert.AreEqual(0, problems.Count, "libdave C API does not match NetCord: " + string.Join("; ", problems));
    }

    [TestMethod]
    public void Libdave_dll_has_the_C_API_NetCord_expects_not_1_3()
    {
        RequireLibdave();
        IntPtr lib = NativeLibrary.Load(LibdavePath);
        try
        {
            // daveSessionAddRecognizedUser postoji tek od 1.3.0, kad su se promijenili potpisi koje NetCord beta.30 zove.
            Assert.IsFalse(NativeLibrary.TryGetExport(lib, "daveSessionAddRecognizedUser", out _),
                "libdave.dll is 1.3 or newer: its C API does not match NetCord 1.0.0-beta.30");
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    [TestMethod]
    public void NetCord_loads_libdave_and_it_supports_DAVE_protocol_1()
    {
        RequireLibdave();

        // Poziv ide kroz NetCordov vlastiti P/Invoke, pa se provjerava i kako NetCord pronalazi libdave.dll.
        MethodInfo? pinvoke = typeof(NetCord.Gateway.GatewayClient).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .FirstOrDefault(m => m.GetCustomAttribute<DllImportAttribute>() is { Value: "libdave" } a
                && (a.EntryPoint ?? m.Name) == "daveMaxSupportedProtocolVersion");
        Assert.IsNotNull(pinvoke, "NetCord has no libdave import for daveMaxSupportedProtocolVersion");
        Assert.AreEqual(0, pinvoke.GetParameters().Length);

        int version = Convert.ToInt32(pinvoke.Invoke(null, null));
        Assert.IsTrue(version >= 1, $"DAVE protocol version {version}");
    }
}
