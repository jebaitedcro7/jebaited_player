using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JebaitedPlayer.Mpv;
using JebaitedPlayer.Subtitles;
using JebaitedPlayer.Sync;
using WebRtcVadSharp;

namespace SyncLab;

/// <summary>
/// Provjera sinkronizacije na stvarnim epizodama (<c>SyncLab real cases.json</c>).
/// <list type="bullet">
/// <item>Referenca: ugrađeni PGS titlovi istog izdanja (<see cref="PgsReference"/>), neovisno o analizi zvuka.</item>
/// <item>Za svaki titl: presuda playera i greška po replici (ono što bi korisnik vidio) na početku, sredini i kraju.</item>
/// <item>Negativni slučajevi: titlovi jedne epizode na zvuku druge moraju biti odbijeni.</item>
/// </list>
/// Datoteka slučajeva i predmemorija nisu u gitu (putanje do tvojih videa); vidi <c>tools\run-real-sync-tests.ps1</c>.
/// </summary>
internal static class RealSuite
{
    private static readonly CultureInfo Hr = CultureInfo.GetCultureInfo("hr-HR");

    /// <summary>Postavke poravnanja (za kalibraciju: --penalty, --jump, --refine-window, --refine-cues).</summary>
    private static AlignOptions Options = new();

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("SyncLab real <cases.json> [--mode 0-3] [--channel dialogue|mix] [--out rezultat.json] [--no-negatives] [--parts]");
            return 2;
        }

        string manifestPath = Path.GetFullPath(args[0]);
        var mode = OperatingMode.Aggressive;
        var channels = AudioChannelMode.Dialogue;
        string? outPath = null;
        bool negatives = true, parts = false;
        int worst = 0;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode": mode = (OperatingMode)int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--out": outPath = Path.GetFullPath(args[++i]); break;
                case "--no-negatives": negatives = false; break;
                case "--parts": parts = true; break;
                case "--worst": worst = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--penalty": Options = Options with { SplitPenalty = double.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
                case "--jump": Options = Options with { JumpPenaltyPerSecond = double.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
                case "--refine-window": Options = Options with { RefineWindowSeconds = double.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
                case "--refine-cues": Options = Options with { RefineMinCuesPerWindow = int.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
                case "--channel": channels = args[++i] == "mix" ? AudioChannelMode.Mix : AudioChannelMode.Dialogue; break;
            }
        }

        Manifest manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Prazna datoteka slučajeva.");
        string cacheDir = Path.GetFullPath(manifest.CacheDir ?? Path.Combine(Path.GetDirectoryName(manifestPath)!, "cache"));
        Directory.CreateDirectory(cacheDir);

        var episodes = new List<Episode>();
        foreach (EpisodeCase e in manifest.Episodes)
        {
            SpeechTrack speech = await LoadSpeechAsync(e.Video, mode, channels, cacheDir);
            List<Cue>? pgs = null;
            if (e.PgsStream is int stream && manifest.Ffprobe is { } ffprobe)
            {
                pgs = await PgsReference.LoadAsync(ffprobe, e.Video, stream, Path.Combine(cacheDir, Key(e.Video, $"pgs{stream}") + ".csv"));
            }

            List<string> subs = e.Subtitles is { Count: > 0 } list ? list : FindSubtitles(e.Video);
            var wrong = new HashSet<string>(e.Wrong ?? [], StringComparer.OrdinalIgnoreCase);
            episodes.Add(new Episode(e.Name, speech, pgs, subs.Select(s => (s, LoadCues(s))).ToList(), wrong));
            Console.WriteLine($"{e.Name}: zvuk {speech.DurationSeconds / 60:0.0} min, govor {speech.SpeechRatio:P0}, PGS replika {pgs?.Count ?? 0}, titlova {subs.Count}");
        }

        var results = new List<CaseResult>();
        Console.WriteLine();
        Console.WriteLine("=== Titlovi za istu epizodu (cilj: prihvaćeno i ≥ 90 % replika unutar 0,5 s)");
        foreach (Episode ep in episodes)
        {
            foreach ((string path, List<Cue> cues) in ep.Subtitles.Where(s => !ep.IsWrong(s.Path)))
            {
                results.Add(EvaluatePositive(ep, path, cues, parts, worst));
            }
        }

        if (episodes.Any(e => e.Subtitles.Any(s => e.IsWrong(s.Path))))
        {
            Console.WriteLine();
            Console.WriteLine("=== Pogrešni titlovi uz video (npr. druga sezona; cilj: odbijeno)");
            foreach (Episode ep in episodes)
            {
                foreach ((string path, List<Cue> cues) in ep.Subtitles.Where(s => ep.IsWrong(s.Path)))
                {
                    results.Add(EvaluateNegative(ep, ep, path, cues));
                }
            }
        }

        if (negatives)
        {
            Console.WriteLine();
            Console.WriteLine("=== Titlovi druge epizode (cilj: odbijeno, ništa se ne primjenjuje)");
            foreach (Episode audio in episodes)
            {
                foreach (Episode other in episodes.Where(o => o != audio))
                {
                    foreach ((string path, List<Cue> cues) in other.Subtitles)
                    {
                        results.Add(EvaluateNegative(audio, other, path, cues));
                    }
                }
            }
        }

        int pos = results.Count(r => r.Kind == "positive"), posOk = results.Count(r => r.Kind == "positive" && r.Ok);
        int neg = results.Count(r => r.Kind == "negative"), negOk = results.Count(r => r.Kind == "negative" && r.Ok);
        Console.WriteLine();
        Console.WriteLine($"UKUPNO: ista epizoda {posOk}/{pos} ispravno, druga epizoda {negOk}/{neg} ispravno odbijeno");

        if (outPath != null)
        {
            await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Rezultati: {outPath}");
        }

        return pos - posOk + neg - negOk;
    }

    private static CaseResult EvaluatePositive(Episode ep, string path, List<Cue> cues, bool parts, int worst)
    {
        string name = ShortName(path);
        var sw = Stopwatch.StartNew();
        SyncResult r = SubtitleAligner.Align(ep.Speech, cues, Options);
        long ms = sw.ElapsedMilliseconds;

        if (ep.Pgs is null)
        {
            Console.WriteLine($"{ep.Name} {name,-34} {r.Verdict,-13} (bez reference) {r.Detail}");
            return new CaseResult("positive", ep.Name, name, r.Verdict.ToString(), r.IsUsable, null, null, null, null, null, null, r.Detail);
        }

        PgsReference.Reference reference = PgsReference.Build(cues, ep.Pgs);
        var known = Enumerable.Range(0, cues.Count).Where(i => reference.Truth[i].HasValue).ToList();
        double duration = ep.Speech.DurationSeconds;
        double[] err = known.Select(i => Math.Abs(Map(r, cues[i].Start) - reference.Truth[i]!.Value)).ToArray();
        double[] original = known.Select(i => Math.Abs(cues[i].Start - reference.Truth[i]!.Value)).ToArray();
        double Within(IEnumerable<double> e, double limit) => e.Any() ? e.Count(x => x <= limit) / (double)e.Count() : double.NaN;

        double[] Third(int t) => known.Select((ci, k) => (ci, k))
            .Where(x => (int)Math.Min(2, reference.Truth[x.ci]!.Value / duration * 3) == t).Select(x => err[x.k]).ToArray();

        double within05 = Within(err, 0.5);
        bool ok = r.IsUsable && within05 >= 0.9;
        Console.WriteLine($"{(ok ? "OK    " : "GREŠKA")} {ep.Name} {name,-34} {r.Verdict,-13} | unutar 0,5 s: {Pct(within05)}, 1 s: {Pct(Within(err, 1.0))} " +
                          $"(početak {Pct(Within(Third(0), 0.5))}, sredina {Pct(Within(Third(1), 0.5))}, kraj {Pct(Within(Third(2), 0.5))}) " +
                          $"| medijan {Median(err):0.00} s, 90 % {Percentile(err, 0.9):0.00} s, najviše {err.DefaultIfEmpty().Max():0.0} s " +
                          $"| original unutar 0,5 s: {Pct(Within(original, 0.5))} | {ms} ms");
        Console.WriteLine($"         dokaz {r.Evidence:0.000}, z {r.FinalScore:0.0}, dijelova {r.Segments.Count} ({r.Segments.Count(s => s.Confirmed)} potvrđeno): " +
                          string.Join(" | ", r.Segments.Select(s => $"{s.Offset:+0.00;-0.00} s od {Clock(s.From)} (z {s.Evidence:0.0}{(s.Confirmed ? "" : ", nepotvrđeno")})")));
        Console.WriteLine($"         referenca: brzina {reference.Speed:0.000000}, {DescribeReference(reference, cues)}");
        Console.WriteLine($"         player: brzina {r.Speed:0.000000}, pomak {r.Offset:0.000} s, ocjene {r.GlobalScore:0.0}/{r.FinalScore:0.0}, {r.Detail}");
        foreach (int k in Enumerable.Range(0, err.Length).OrderByDescending(k => err[k]).Take(worst).OrderBy(k => known[k]))
        {
            int ci = known[k];
            Console.WriteLine($"           replika {Clock(cues[ci].Start)} ({cues[ci].Start:0.0} s, do {cues[ci].End - cues[ci].Start:0.0} s): " +
                              $"player {Map(r, cues[ci].Start) - cues[ci].Start:+0.00;-0.00} s, referenca {reference.Truth[ci]!.Value - cues[ci].Start:+0.00;-0.00} s, " +
                              $"greška {err[k]:0.00} s; pauza prije {(ci > 0 ? cues[ci].Start - cues[ci - 1].End : 0):0.0} s");
        }

        if (parts)
        {
            foreach (SyncPart p in r.Parts)
            {
                Console.WriteLine($"           {p.Start,7:0} – {p.End,7:0} s: {(p.Residual is double d ? $"{d * 1000,6:0} ms" : "  nejasno")} ocjena {p.Score:0.00} {(p.Agrees ? "✓" : "✗")}");
            }
        }

        return new CaseResult("positive", ep.Name, name, r.Verdict.ToString(), ok, within05, Within(Third(0), 0.5), Within(Third(1), 0.5),
            Within(Third(2), 0.5), Median(err), Within(original, 0.5), r.Detail);
    }

    private static CaseResult EvaluateNegative(Episode audio, Episode owner, string path, List<Cue> cues)
    {
        SyncResult r = SubtitleAligner.Align(audio.Speech, cues, Options);
        bool ok = !r.IsUsable;
        string name = $"{owner.Name}:{ShortName(path)}";
        Console.WriteLine($"{(ok ? "OK    " : "GREŠKA")} zvuk {audio.Name} ← {name,-38} {r.Verdict,-13} dokaz {r.Evidence:0.000}, z {r.FinalScore:0.0}, dijelova {r.Segments.Count} {r.Detail}");
        return new CaseResult("negative", audio.Name, name, r.Verdict.ToString(), ok, null, null, null, null, null, null, r.Detail);
    }

    /// <summary>Što korisnik vidi: primijenjena korekcija samo kad je presuda prihvaćena, inače izvorna vremena.</summary>
    private static double Map(SyncResult r, double t) => r.IsUsable ? r.Apply(t) : t;

    private static string DescribeReference(PgsReference.Reference reference, List<Cue> cues)
    {
        // Susjedni dijelovi sa sličnim pomakom (±0,3 s) spajaju se u jedan.
        var segments = new List<(double From, double To, double Shift)>();
        foreach (PgsReference.Window w in reference.Windows.Where(w => w.Overlap >= 0.45))
        {
            double from = cues[w.From].Start * reference.Speed + w.Shift, to = cues[w.To - 1].End * reference.Speed + w.Shift;
            if (segments.Count > 0 && Math.Abs(segments[^1].Shift - w.Shift) <= 0.3)
            {
                segments[^1] = (segments[^1].From, to, segments[^1].Shift);
            }
            else
            {
                segments.Add((from, to, w.Shift));
            }
        }

        double mean = reference.Windows.Average(w => w.Overlap);
        return (segments.Count == 0
            ? "nije pronađena"
            : string.Join(" | ", segments.Select(s => $"{s.Shift.ToString("+0.00;−0.00", Hr)} s ({Clock(s.From)}–{Clock(s.To)})")))
            + $" [poklapanje s PGS {mean:0.00}, poznato {reference.Truth.Count(t => t.HasValue)}/{reference.Truth.Length} replika]";
    }

    private static async Task<SpeechTrack> LoadSpeechAsync(string video, OperatingMode mode, AudioChannelMode channels, string cacheDir)
    {
        string file = Path.Combine(cacheDir, Key(video, $"vad-{(channels == AudioChannelMode.Mix ? "mix" : "dialog")}-m{(int)mode}") + ".vad");
        if (File.Exists(file))
        {
            await using FileStream fs = File.OpenRead(file);
            return SpeechTrack.Load(fs);
        }

        using var detector = new SpeechDetector(mode);
        await MpvAudioExtractor.ExtractAsync(video, null, null, null, (buf, n) => detector.Push(buf, n), null, CancellationToken.None, channels);
        SpeechTrack speech = detector.Finish();
        await using (FileStream fs = File.Create(file))
        {
            speech.Save(fs);
        }

        return speech;
    }

    private static List<Cue> LoadCues(string path)
    {
        string text = SubtitleEncoding.Decode(File.ReadAllBytes(path), null, out _);
        SubtitleDocument doc = SubtitleParser.Parse(text, path);
        return doc.Cues.Select(c => new Cue(c.StartMs / 1000.0, c.EndMs / 1000.0)).Where(c => c.End > c.Start)
            .OrderBy(c => c.Start).ToList();
    }

    private static List<string> FindSubtitles(string video)
    {
        string dir = Path.GetDirectoryName(video)!, stem = Path.GetFileNameWithoutExtension(video);
        return Directory.EnumerateFiles(dir, stem + ".*")
            .Where(f => SubtitleFormats.IsSubtitleFile(f) && !f.Contains(".sync.", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ShortName(string path)
    {
        // "Serija.S01E05.1080p.WEB-GRP.hr.srt" -> "hr.srt" (dio iza oznake grupe izdanja).
        string name = Path.GetFileName(path);
        int dash = name.LastIndexOf('-');
        int dot = dash >= 0 ? name.IndexOf('.', dash) : -1;
        return dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
    }

    private static string Key(string video, string kind)
    {
        var info = new FileInfo(video);
        string raw = $"{info.FullName.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{kind}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..12].ToLowerInvariant();
        return $"{Path.GetFileNameWithoutExtension(video)[..Math.Min(24, Path.GetFileNameWithoutExtension(video).Length)]}_{kind}_{hash}";
    }

    private static string Pct(double v) => double.IsNaN(v) ? "—" : v.ToString("P0", Hr);

    private static string Clock(double s) => TimeSpan.FromSeconds(Math.Max(0, s)).ToString(@"m\:ss", CultureInfo.InvariantCulture);

    private static double Median(double[] v) => Percentile(v, 0.5);

    private static double Percentile(double[] v, double p)
    {
        if (v.Length == 0)
        {
            return double.NaN;
        }

        double[] s = [.. v.Order()];
        return s[(int)Math.Clamp(Math.Round(p * (s.Length - 1)), 0, s.Length - 1)];
    }

    private sealed record Episode(string Name, SpeechTrack Speech, List<Cue>? Pgs, List<(string Path, List<Cue> Cues)> Subtitles,
        HashSet<string> Wrong)
    {
        /// <summary>Titl uz video koji nije za ovu epizodu (npr. druga sezona); player ga mora odbiti.</summary>
        public bool IsWrong(string path) => Wrong.Contains(Path.GetFileName(path));
    }

    public sealed record CaseResult(string Kind, string Episode, string Subtitle, string Verdict, bool Ok, double? Within05,
        double? Start05, double? Middle05, double? End05, double? MedianError, double? Original05, string Detail);

    private sealed class Manifest
    {
        public string? Ffprobe { get; set; }
        public string? CacheDir { get; set; }
        public List<EpisodeCase> Episodes { get; set; } = [];
    }

    private sealed class EpisodeCase
    {
        public string Name { get; set; } = "";
        public string Video { get; set; } = "";
        public int? PgsStream { get; set; }

        /// <summary>Prazno = svi titlovi uz video istog naziva.</summary>
        public List<string>? Subtitles { get; set; }

        /// <summary>Nazivi datoteka uz video koje nisu za ovu epizodu (moraju biti odbijene).</summary>
        public List<string>? Wrong { get; set; }
    }
}
