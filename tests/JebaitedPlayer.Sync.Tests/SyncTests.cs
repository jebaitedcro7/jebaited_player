using JebaitedPlayer.Sync;

namespace JebaitedPlayer.Sync.Tests;

/// <summary>Sintetički "film": poznato gdje se govori i koje replike tome odgovaraju.</summary>
internal static class Film
{
    public const double Ntsc = 24000.0 / 1001.0;

    /// <param name="vadNoise">Udio okvira koje detektor pogrešno označi.</param>
    /// <param name="musicShare">Udio vremena s glazbom koju detektor djelomično vidi kao govor.</param>
    public static (SpeechTrack Speech, List<Cue> Cues) Make(int seed, double minutes, double vadNoise = 0.05, double musicShare = 0.1)
    {
        var rng = new Random(seed);
        int length = (int)(minutes * 60 * 100);
        var frames = new byte[length];
        var cues = new List<Cue>();
        double duration = minutes * 60;
        double t = 5 + rng.NextDouble() * 20;
        while (t < duration - 10)
        {
            double len = 0.8 + rng.NextDouble() * 3.5;
            for (double x = t; x < t + len; x += 0.01)
            {
                frames[(int)(x * 100)] = 1;
            }

            // Kratke stanke unutar rečenice.
            for (int p = 0; p < (int)len; p++)
            {
                double ps = t + rng.NextDouble() * len;
                for (double x = ps; x < Math.Min(ps + 0.15, t + len); x += 0.01)
                {
                    frames[(int)(x * 100)] = 0;
                }
            }

            double start = t + (rng.NextDouble() * 0.25 - 0.15);
            double end = t + len + 0.15 + rng.NextDouble() * 0.3;
            cues.Add(new Cue(Math.Max(0, start), end));

            double gap = rng.NextDouble() < 0.08 ? 10 + rng.NextDouble() * 50 : 0.3 + -Math.Log(1 - rng.NextDouble()) * 2.2;
            t += len + gap;
        }

        // Glazba i efekti: dijelovi u kojima detektor često "čuje govor".
        double music = 0;
        while (music < musicShare * duration)
        {
            double ms = rng.NextDouble() * (duration - 90);
            double ml = 20 + rng.NextDouble() * 70;
            for (double x = ms; x < ms + ml; x += 0.01)
            {
                if (rng.NextDouble() < 0.6)
                {
                    frames[(int)(x * 100)] = 1;
                }
            }

            music += ml;
        }

        // Pogreške detektora u nizovima (ne pojedinačni okviri).
        int flips = (int)(vadNoise * length / 15);
        for (int f = 0; f < flips; f++)
        {
            int at = rng.Next(length - 30);
            int run = 5 + rng.Next(20);
            for (int i = at; i < at + run; i++)
            {
                frames[i] ^= 1;
            }
        }

        return (new SpeechTrack(frames), cues);
    }

    /// <summary>Titlovi kakvi bi bili u datoteci kad je ispravno prikazano vrijeme t = izvorno × speed + offset.</summary>
    public static List<Cue> Distort(List<Cue> truth, double speed, double offset) =>
        truth.Select(c => new Cue((c.Start - offset) / speed, (c.End - offset) / speed))
            .Where(c => c.Start >= 0).ToList();

    /// <summary>Za svako ciljano vrijeme: početak prve replike nakon najbliže pauze od barem 5 s (kraj čina, prijelaz scene).</summary>
    public static double[] PausesNear(List<Cue> cues, double[] targets) =>
        targets.Select(t => Enumerable.Range(1, cues.Count - 1)
            .Where(i => cues[i].Start - cues[i - 1].End >= 5)
            .Select(i => cues[i].Start)
            .OrderBy(s => Math.Abs(s - t))
            .First()).ToArray();

    public static double MaxError(SyncResult r, List<Cue> distorted, List<Cue> truth, double speed, double offset)
    {
        double max = 0;
        foreach (Cue c in distorted)
        {
            double expected = c.Start * speed + offset;
            max = Math.Max(max, Math.Abs(r.Apply(c.Start) - expected));
        }

        return max;
    }
}

[TestClass]
public class AlignerTests
{
    [TestMethod]
    public void Already_synced_subtitles_are_confirmed()
    {
        var (speech, cues) = Film.Make(1, 45);

        SyncResult r = SubtitleAligner.Align(speech, cues);

        Assert.AreEqual(SyncVerdict.InSync, r.Verdict, Describe(r));
        Assert.IsTrue(r.MaxShiftSeconds < 0.12, Describe(r));
        Assert.IsTrue(r.MatchRatio > r.ChanceMatchRatio + 0.2, Describe(r));
    }

    [DataTestMethod]
    [DataRow(4.37)]
    [DataRow(-63.2)]
    [DataRow(0.6)]
    [DataRow(-245.0)]
    public void Constant_offset_is_found(double offset)
    {
        var (speech, truth) = Film.Make(2, 45);
        List<Cue> subs = Film.Distort(truth, 1, offset);

        SyncResult r = SubtitleAligner.Align(speech, subs);

        Assert.AreEqual(SyncVerdict.Corrected, r.Verdict, Describe(r));
        Assert.AreEqual(offset, r.Offset, 0.06, Describe(r));
        Assert.AreEqual(1, r.Speed, 0.0002, Describe(r));
    }

    [DataTestMethod]
    [DataRow(25 / Film.Ntsc, 1.5)]
    [DataRow(Film.Ntsc / 25, -3.0)]
    [DataRow(24 / Film.Ntsc, 0)]
    [DataRow(25.0 / 24, 12.0)]
    public void Different_fps_is_corrected(double speed, double offset)
    {
        var (speech, truth) = Film.Make(3, 95);
        List<Cue> subs = Film.Distort(truth, speed, offset);

        SyncResult r = SubtitleAligner.Align(speech, subs);

        Assert.AreEqual(SyncVerdict.Corrected, r.Verdict, Describe(r));
        Assert.IsTrue(Film.MaxError(r, subs, truth, speed, offset) < 0.1, $"greška {Film.MaxError(r, subs, truth, speed, offset):0.000} s; {Describe(r)}");
    }

    [TestMethod]
    public void Unusual_drift_is_measured_from_parts_of_the_film()
    {
        // Nije nijedan standardni omjer: grubo traženje nađe 1,0, a dijelovi filma otkriju drift.
        var (speech, truth) = Film.Make(4, 100);
        List<Cue> subs = Film.Distort(truth, 1.0007, -2);

        SyncResult r = SubtitleAligner.Align(speech, subs);

        Assert.AreEqual(SyncVerdict.Corrected, r.Verdict, Describe(r));
        Assert.IsTrue(Film.MaxError(r, subs, truth, 1.0007, -2) < 0.1, $"greška {Film.MaxError(r, subs, truth, 1.0007, -2):0.000} s; {Describe(r)}");
    }

    [TestMethod]
    public void Subtitles_for_another_film_are_rejected()
    {
        for (int seed = 10; seed < 16; seed++)
        {
            var (speech, _) = Film.Make(seed, 60);
            var (_, other) = Film.Make(seed + 100, 60);

            SyncResult r = SubtitleAligner.Align(speech, other);

            Assert.AreEqual(SyncVerdict.Mismatch, r.Verdict, $"seed {seed}: {Describe(r)}");
            Assert.IsFalse(r.IsUsable);
        }
    }

    [TestMethod]
    public void Different_edit_in_the_middle_is_corrected_in_parts()
    {
        // Titlovi za verziju u kojoj je scena na polovici 25 s dulja: druga polovica kasni 25 s.
        var (speech, truth) = Film.Make(5, 90);
        double cut = 45 * 60;
        List<Cue> subs = truth.Select(c => c.Start < cut ? c : new Cue(c.Start + 25, c.End + 25)).ToList();

        SyncResult r = SubtitleAligner.Align(speech, subs);

        Assert.AreEqual(SyncVerdict.CorrectedInParts, r.Verdict, Describe(r));
        Assert.AreEqual(2, r.Segments.Count, Describe(r));
        double maxError = subs.Zip(truth).Max(p => Math.Abs(r.Map(p.First.Start) - p.Second.Start));
        Assert.IsTrue(maxError < 0.1, $"greška {maxError:0.000} s; {Describe(r)}");
    }

    [TestMethod]
    public void Act_breaks_of_another_release_are_corrected_in_parts()
    {
        // Kao stvarna epizoda: titlovi za TV izdanje, a u WEB izdanju je svaka pauza između činova dulja 2,5–4,5 s.
        var (speech, truth) = Film.Make(21, 44);
        double[] breaks = Film.PausesNear(truth, [7.7 * 60, 16 * 60, 26.2 * 60, 33 * 60]);
        double[] extra = [3.1, 2.5, 4.1, 4.4];
        List<Cue> subs = truth.Select(c =>
        {
            double shift = breaks.Select((b, i) => c.Start >= b ? extra[i] : 0).Sum();
            return new Cue(c.Start - shift, c.End - shift);
        }).ToList();

        SyncResult r = SubtitleAligner.Align(speech, subs);

        Assert.AreEqual(SyncVerdict.CorrectedInParts, r.Verdict, Describe(r));
        double[] errors = subs.Zip(truth).Select(p => Math.Abs(r.Map(p.First.Start) - p.Second.Start)).ToArray();
        double within = errors.Count(e => e <= 0.2) / (double)errors.Length;
        Assert.IsTrue(within >= 0.97, $"unutar 0,2 s: {within:P0}, najviše {errors.Max():0.00} s; {Describe(r)}");
    }

    [TestMethod]
    public void Act_breaks_with_pal_speed_are_corrected_in_parts()
    {
        // Titlovi za PAL DVD (25 fps) i uz to druga montaža na dva mjesta.
        var (speech, truth) = Film.Make(22, 44);
        double speed = 25 / Film.Ntsc;
        double[] breaks = Film.PausesNear(truth, [12 * 60, 30 * 60]);
        double[] extra = [3.0, 5.5];
        List<Cue> subs = truth.Select(c =>
        {
            double shift = breaks.Select((b, i) => c.Start >= b ? extra[i] : 0).Sum();
            return new Cue((c.Start - shift) / speed, (c.End - shift) / speed);
        }).ToList();

        SyncResult r = SubtitleAligner.Align(speech, subs);

        Assert.AreEqual(SyncVerdict.CorrectedInParts, r.Verdict, Describe(r));
        double[] errors = subs.Zip(truth).Select(p => Math.Abs(r.Map(p.First.Start) - p.Second.Start)).ToArray();
        double within = errors.Count(e => e <= 0.2) / (double)errors.Length;
        Assert.IsTrue(within >= 0.97, $"unutar 0,2 s: {within:P0}, najviše {errors.Max():0.00} s; {Describe(r)}");
    }

    [TestMethod]
    public void Subtitles_for_another_film_are_rejected_even_with_parts_allowed()
    {
        // Poravnanje po dijelovima ima više slobode, pa mora i dalje odbiti titlove drugog filma i kraćih epizoda.
        for (int seed = 30; seed < 36; seed++)
        {
            var (speech, _) = Film.Make(seed, 22 + seed % 3 * 11, vadNoise: 0.1, musicShare: 0.25);
            var (_, other) = Film.Make(seed + 100, 22 + seed % 3 * 11);

            SyncResult r = SubtitleAligner.Align(speech, other);

            Assert.IsFalse(r.IsUsable, $"seed {seed}: {Describe(r)}");
        }
    }

    [TestMethod]
    public void Noisy_detector_and_lots_of_music_still_align()
    {
        var (speech, truth) = Film.Make(6, 60, vadNoise: 0.15, musicShare: 0.3);
        List<Cue> subs = Film.Distort(truth, 1, 7.25);

        SyncResult r = SubtitleAligner.Align(speech, subs);

        Assert.AreEqual(SyncVerdict.Corrected, r.Verdict, Describe(r));
        Assert.AreEqual(7.25, r.Offset, 0.08, Describe(r));
    }

    [TestMethod]
    public void Short_episode_is_aligned()
    {
        var (speech, truth) = Film.Make(7, 22);
        List<Cue> subs = Film.Distort(truth, 1, -1.8);

        SyncResult r = SubtitleAligner.Align(speech, subs);

        Assert.AreEqual(SyncVerdict.Corrected, r.Verdict, Describe(r));
        Assert.AreEqual(-1.8, r.Offset, 0.06, Describe(r));
    }

    [TestMethod]
    public void Too_few_cues_is_not_enough_data()
    {
        var (speech, truth) = Film.Make(8, 30);

        SyncResult r = SubtitleAligner.Align(speech, truth.Take(8).ToList());

        Assert.AreEqual(SyncVerdict.NotEnoughData, r.Verdict);
    }

    [TestMethod]
    public void Overlapping_and_invalid_cues_are_normalized()
    {
        List<Cue> n = SubtitleAligner.Normalize([new Cue(5, 6), new Cue(1, 2), new Cue(1.5, 3), new Cue(4, 4), new Cue(-1, 0.5)]);

        CollectionAssert.AreEqual(new[] { new Cue(1, 3), new Cue(5, 6) }, n);
    }

    internal static string Describe(SyncResult r) =>
        $"{r.Verdict}: brzina {r.Speed:0.000000}, pomak {r.Offset:0.000} s, global {r.GlobalScore:0.0}, final {r.FinalScore:0.0}, " +
        $"dokaz {r.Evidence:0.000}, dijelovi {r.AgreeingWindows}/{r.Windows}, replike {r.MatchRatio:P0} (slučajno {r.ChanceMatchRatio:P0}), " +
        $"{r.Detail}; segmenti: {string.Join(" | ", r.Segments.Select(s => $"{s.Offset:+0.00;-0.00} s od {s.From:0} s"))}";
}

[TestClass]
public class SpeechTrackAndFftTests
{
    [TestMethod]
    public void Speech_track_survives_save_and_load()
    {
        var (speech, _) = Film.Make(9, 3);
        using var ms = new MemoryStream();
        speech.Save(ms);
        ms.Position = 0;

        SpeechTrack loaded = SpeechTrack.Load(ms);

        Assert.AreEqual(speech.Length, loaded.Length);
        Assert.IsTrue(speech.Frames.SequenceEqual(loaded.Frames));
        Assert.IsTrue(ms.Length < speech.Length / 7);
    }

    [TestMethod]
    public void Corrupted_speech_cache_is_rejected()
    {
        using var ms = new MemoryStream([1, 2, 3, 4, 5]);
        Assert.ThrowsException<InvalidDataException>(() => SpeechTrack.Load(ms));
    }

    [TestMethod]
    public void Fft_cross_correlation_matches_direct_sum()
    {
        var rng = new Random(3);
        double[] a = Enumerable.Range(0, 300).Select(_ => rng.NextDouble() - 0.5).ToArray();
        double[] s = Enumerable.Range(0, 200).Select(_ => rng.NextDouble() - 0.5).ToArray();
        int maxLag = 50;
        int n = Fft.NextPowerOfTwo(Math.Max(a.Length, s.Length) + maxLag + 1);

        double[] c = Fft.CrossCorrelate(Fft.Spectrum(a, n), s, maxLag);

        for (int d = -maxLag; d <= maxLag; d++)
        {
            double direct = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int j = i + d;
                if (j >= 0 && j < a.Length)
                {
                    direct += s[i] * a[j];
                }
            }

            Assert.AreEqual(direct, c[d + maxLag], 1e-9, $"d = {d}");
        }
    }

    [TestMethod]
    public void Detector_hears_voice_like_sound_and_not_silence()
    {
        // 3 s tišine (lagani šum), 3 s "glasa" (harmonici 140 Hz, slogovi 4 Hz), 3 s tišine.
        const int rate = 16000;
        var rng = new Random(1);
        short[] samples = new short[rate * 9];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = i / (double)rate;
            double noise = (rng.NextDouble() - 0.5) * 60;
            double voice = 0;
            if (t is >= 3 and < 6)
            {
                double envelope = 0.5 + 0.5 * Math.Sin(2 * Math.PI * 4 * t);
                for (int h = 1; h <= 12; h++)
                {
                    voice += Math.Sin(2 * Math.PI * 140 * h * t) / h;
                }

                voice *= 5000 * envelope;
            }

            samples[i] = (short)Math.Clamp(voice + noise, short.MinValue, short.MaxValue);
        }

        using var detector = new SpeechDetector();
        detector.Push(samples, samples.Length);
        SpeechTrack track = detector.Finish();

        Assert.AreEqual(900, track.Length);
        double silence = (Count(track, 20, 300) + Count(track, 620, 900)) / 560.0;
        double voiced = Count(track, 300, 600) / 300.0;
        Assert.IsTrue(voiced > 0.5, $"glas: {voiced:P0}");
        Assert.IsTrue(silence < 0.1, $"tišina: {silence:P0}");
    }

    private static int Count(SpeechTrack t, int from, int to)
    {
        int n = 0;
        for (int i = from; i < to; i++)
        {
            n += t.Frames[i];
        }

        return n;
    }
}
