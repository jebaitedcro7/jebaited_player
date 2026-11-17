using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using JebaitedPlayer.Mpv;
using JebaitedPlayer.Subtitles;
using JebaitedPlayer.Sync;
using WebRtcVadSharp;

// Uporaba: SyncLab <video> [--aid N] [--mode 0-3] [--channel dialogue|mix] [--expected expected.json] [--cache file.vad] <titl.srt> ...
//          SyncLab real <cases.json> [--mode 0-3] [--out rezultat.json] [--no-negatives] [--parts]   (stvarne epizode)
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("hr-HR");
Console.OutputEncoding = System.Text.Encoding.UTF8;
if (args.Length > 0 && args[0] == "real")
{
    return await SyncLab.RealSuite.RunAsync(args[1..]);
}

string? video = null, aid = null, expectedPath = null, cache = null;
var mode = OperatingMode.Aggressive;
var channels = AudioChannelMode.Dialogue;
var subs = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--aid": aid = args[++i]; break;
        case "--mode": mode = (OperatingMode)int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--expected": expectedPath = args[++i]; break;
        case "--cache": cache = args[++i]; break;
        case "--channel": channels = args[++i] == "mix" ? AudioChannelMode.Mix : AudioChannelMode.Dialogue; break;
        default:
            if (video is null) video = args[i]; else subs.Add(args[i]);
            break;
    }
}

if (video is null)
{
    Console.WriteLine("SyncLab <video> [--aid N] [--mode 0-3] [--channel dialogue|mix] [--expected expected.json] [--cache file.vad] <titl.srt> ...");
    return 2;
}

SpeechTrack speech;
var proc = Process.GetCurrentProcess();
if (cache != null && File.Exists(cache))
{
    using FileStream fs = File.OpenRead(cache);
    speech = SpeechTrack.Load(fs);
    Console.WriteLine($"Govor iz predmemorije: {speech.DurationSeconds:0.0} s");
}
else
{
    var sw = Stopwatch.StartNew();
    TimeSpan cpu0 = proc.TotalProcessorTime;
    using var detector = new SpeechDetector(mode);
    AudioExtraction extraction = await MpvAudioExtractor.ExtractAsync(video, aid, null, null, (buf, n) => detector.Push(buf, n), null,
        CancellationToken.None, channels);
    long samples = extraction.Samples;
    speech = detector.Finish();
    proc.Refresh();
    double audioSec = samples / 16000.0;
    Console.WriteLine($"Zvuk: {audioSec:0.0} s za {sw.Elapsed.TotalSeconds:0.00} s ({audioSec / sw.Elapsed.TotalSeconds:0}× brže od stvarnog), " +
        $"CPU {(proc.TotalProcessorTime - cpu0).TotalSeconds:0.00} s, RAM {proc.WorkingSet64 / 1048576} MB, govor {speech.SpeechRatio:P0} (VAD način {mode}, {(extraction.DialogueOnly ? "središnji kanal" : "svi kanali")}, izvor {extraction.ChannelLayout ?? "?"})");
    if (cache != null)
    {
        using FileStream fs = File.Create(cache);
        speech.Save(fs);
    }
}

Dictionary<string, JsonElement>? expected = expectedPath is null ? null
    : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(expectedPath));
int failures = 0;
foreach (string path in subs)
{
    string text = SubtitleEncoding.Decode(File.ReadAllBytes(path), null, out _);
    SubtitleDocument doc = SubtitleParser.Parse(text, path);
    var cues = doc.Cues.Select(c => new Cue(c.StartMs / 1000.0, c.EndMs / 1000.0)).ToList();
    var sw = Stopwatch.StartNew();
    SyncResult r = SubtitleAligner.Align(speech, cues);
    string line = $"{Path.GetFileName(path),-24} {r.Verdict,-13} brzina {r.Speed:0.000000} pomak {r.Offset,8:0.000} s | global {r.GlobalScore,5:0.0} final {r.FinalScore,5:0.0} " +
        $"dijelovi {r.AgreeingWindows}/{r.Windows} replike {r.MatchRatio:P0} (slučajno {r.ChanceMatchRatio:P0}) pouzd. {r.Confidence} | {sw.ElapsedMilliseconds} ms";
    if (expected != null && expected.TryGetValue(Path.GetFileName(path), out JsonElement e))
    {
        string want = e.GetProperty("verdict").GetString()!;
        bool ok = want == r.Verdict.ToString();
        if (ok && e.TryGetProperty("speed", out JsonElement sp))
        {
            double k = sp.GetDouble(), d = e.GetProperty("offset").GetDouble();
            double err = cues.Max(c => Math.Abs(r.Apply(c.Start) - (k * c.Start + d)));
            line += $" | greška najviše {err * 1000:0} ms";
            ok = err < 0.1;
        }

        line = (ok ? "OK      " : "GREŠKA  ") + line + (ok ? string.Empty : $" (očekivano {want})");
        failures += ok ? 0 : 1;
    }

    Console.WriteLine(line);
    if (Environment.GetEnvironmentVariable("SYNCLAB_PARTS") == "1")
    {
        foreach (SyncPart p in r.Parts)
        {
            Console.WriteLine($"         {p.Start,7:0} – {p.End,7:0} s: {(p.Residual is double d ? $"{d * 1000,6:0} ms" : "  nejasno")} ocjena {p.Score:0.00} {(p.Agrees ? "✓" : "✗")}");
        }
    }
}

return failures;
