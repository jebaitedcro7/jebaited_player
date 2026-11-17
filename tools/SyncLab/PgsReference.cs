using System.Diagnostics;
using System.Globalization;
using JebaitedPlayer.Sync;

namespace SyncLab;

/// <summary>
/// Neovisna referenca za stvarne epizode: ugrađeni slikovni titlovi (PGS) usklađeni su s upravo tim izdanjem videa.
/// Prijevod ima uglavnom iste replike, pa usporedba vremena replika daje točan pomak po dijelovima, bez analize zvuka.
/// </summary>
internal static class PgsReference
{
    private const int Rate = 100;

    public static readonly double[] Speeds = BuildSpeeds();

    /// <summary>Vremena PGS replika iz videa (ffprobe), spremljena u predmemoriju.</summary>
    public static async Task<List<Cue>> LoadAsync(string ffprobe, string video, int stream, string cacheFile)
    {
        string[] lines;
        if (File.Exists(cacheFile))
        {
            lines = await File.ReadAllLinesAsync(cacheFile);
        }
        else
        {
            var psi = new ProcessStartInfo(ffprobe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string a in new[] { "-v", "error", "-select_streams", $"s:{stream}", "-show_entries", "packet=pts_time,size", "-of", "csv=p=0", video })
            {
                psi.ArgumentList.Add(a);
            }

            using Process p = Process.Start(psi) ?? throw new InvalidOperationException("ffprobe se nije pokrenuo.");
            string output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (p.ExitCode != 0)
            {
                throw new InvalidOperationException($"ffprobe nije uspio: {await p.StandardError.ReadToEndAsync()}");
            }

            lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            await File.WriteAllLinesAsync(cacheFile, lines);
        }

        // Paket sa slikom (veći) počinje repliku, mali paket (bez slike) je briše.
        var packets = lines.Select(l => l.Split(','))
            .Where(f => f.Length >= 2 && double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            .Select(f => (Time: double.Parse(f[0], CultureInfo.InvariantCulture), Size: int.Parse(f[1], CultureInfo.InvariantCulture)))
            .OrderBy(p => p.Time).ToList();
        var cues = new List<Cue>();
        for (int i = 0; i < packets.Count; i++)
        {
            if (packets[i].Size <= 100)
            {
                continue;
            }

            double end = packets.Skip(i + 1).Where(p => p.Size <= 100).Select(p => p.Time).DefaultIfEmpty(packets[i].Time + 2).First();
            if (end > packets[i].Time)
            {
                cues.Add(new Cue(packets[i].Time, end));
            }
        }

        return cues;
    }

    /// <summary>Referentno vrijeme početka za svaku repliku (null = nije pouzdano utvrđeno) i pronađena brzina.</summary>
    public static Reference Build(IReadOnlyList<Cue> cues, IReadOnlyList<Cue> pgs)
    {
        double maxEnd = Math.Max(pgs[^1].End, cues.Max(c => c.End) * 1.05);
        int n = (int)((maxEnd + 700) * Rate);
        int[] prefix = Prefix(pgs, n);

        // 1) Za svaku brzinu: grubi pomak cijele datoteke, pa pomak po dijelovima. Pobjeđuje brzina s najboljim
        //    prosječnim poklapanjem dijelova (pomaci po dijelovima upiju i male razlike u brzini, pa 1,000 ima prednost
        //    pred 1,001 osim ako je jasno lošija).
        (double Speed, List<Window> Windows, double Mean) best = (1, [], -1);
        foreach (double k in Speeds)
        {
            (int[] a, int[] b) = Frames(cues, 0, cues.Count, k);
            (int Shift, double Score) global = (0, -1);
            for (int d = -600 * Rate; d <= 600 * Rate; d += 10)
            {
                double s = Jaccard(prefix, a, b, d, n);
                if (s > global.Score)
                {
                    global = (d, s);
                }
            }

            List<Window> measured = MeasureWindows(cues, prefix, n, k, global.Shift);
            double mean = measured.Average(w => w.Overlap);
            bool better = k == 1.0 ? mean > best.Mean : mean > best.Mean + (best.Speed == 1.0 ? 0.03 : 0);
            if (best.Mean < 0 || better)
            {
                best = (k, measured, mean);
            }
        }

        double speed = best.Speed;
        List<Window> windowsBest = best.Windows;

        // 2) Svaka replika: pomak svog ili susjednog dijela koji je najbolje poklapa s PGS replikom.
        List<Window> windows = windowsBest;
        var truth = new double?[cues.Count];
        // Pomak susjednog dijela dolazi u obzir samo za replike uz rub dijela (granica čina može pasti unutar dijela)
        // i samo kad je poklapanje jasno bolje; inače bi pojedine replike slučajno "pobjegle" u susjedni pomak.
        for (int wi = 0; wi < windows.Count; wi++)
        {
            Window own = windows[wi];
            if (own.Overlap < 0.45)
            {
                continue;
            }

            for (int ci = own.From; ci < own.To; ci++)
            {
                (int[] a, int[] b) = Frames(cues, ci, ci + 1, speed);
                double ownScore = Jaccard(prefix, a, b, (int)Math.Round(own.Shift * Rate), n, margin: 30);
                (double Shift, double Score) pick = (own.Shift, ownScore);
                bool nearStart = ci - own.From < 4, nearEnd = own.To - 1 - ci < 4;
                foreach (int ni in new[] { nearStart ? wi - 1 : -1, nearEnd ? wi + 1 : -1 })
                {
                    if (ni < 0 || ni >= windows.Count || windows[ni].Overlap < 0.45)
                    {
                        continue;
                    }

                    double s = Jaccard(prefix, a, b, (int)Math.Round(windows[ni].Shift * Rate), n, margin: 30);
                    if (s >= ownScore + 0.25 && s > pick.Score)
                    {
                        pick = (windows[ni].Shift, s);
                    }
                }

                if (pick.Score >= 0.3)
                {
                    truth[ci] = cues[ci].Start * speed + pick.Shift;
                }
            }
        }

        return new Reference(speed, truth, windows);
    }

    /// <summary>Pomak po dijelovima (≥ 10 replika i ≥ 90 s), ±40 s oko grubog pomaka, korak 20 ms.</summary>
    private static List<Window> MeasureWindows(IReadOnlyList<Cue> cues, int[] prefix, int n, double speed, int around)
    {
        var windows = new List<Window>();
        int start = 0;
        while (start < cues.Count)
        {
            int end = start;
            while (end < cues.Count && (end - start < 10 || cues[end].Start - cues[start].Start < 90))
            {
                end++;
            }

            if (cues.Count - end < 10)
            {
                end = cues.Count;
            }

            (int[] a, int[] b) = Frames(cues, start, end, speed);
            (int Shift, double Score) w = (around, -1);
            for (int d = around - 40 * Rate; d <= around + 40 * Rate; d += 2)
            {
                double s = Jaccard(prefix, a, b, d, n);
                if (s > w.Score)
                {
                    w = (d, s);
                }
            }

            windows.Add(new Window(start, end, w.Shift / (double)Rate, w.Score));
            start = end;
        }

        return windows;
    }

    private static int[] Prefix(IReadOnlyList<Cue> cues, int n)
    {
        var cover = new byte[n];
        foreach (Cue c in cues)
        {
            for (int i = Math.Max(0, (int)(c.Start * Rate)); i < Math.Min(n, (int)(c.End * Rate)); i++)
            {
                cover[i] = 1;
            }
        }

        var p = new int[n + 1];
        for (int i = 0; i < n; i++)
        {
            p[i + 1] = p[i] + cover[i];
        }

        return p;
    }

    private static (int[] A, int[] B) Frames(IReadOnlyList<Cue> cues, int from, int to, double speed)
    {
        var a = new int[to - from];
        var b = new int[to - from];
        for (int i = from; i < to; i++)
        {
            a[i - from] = (int)Math.Round(cues[i].Start * speed * Rate);
            b[i - from] = (int)Math.Round(cues[i].End * speed * Rate);
        }

        return (a, b);
    }

    /// <summary>Presjek / unija pokrivenosti replika (pomaknutih za d okvira) i PGS replika u istom rasponu.</summary>
    private static double Jaccard(int[] prefix, int[] a, int[] b, int d, int n, int margin = 200)
    {
        long inter = 0, own = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int lo = Math.Clamp(a[i] + d, 0, n), hi = Math.Clamp(b[i] + d, 0, n);
            inter += prefix[hi] - prefix[lo];
            own += b[i] - a[i];
        }

        int w0 = Math.Clamp(a[0] + d - margin, 0, n), w1 = Math.Clamp(b[^1] + d + margin, 0, n);
        long union = own + (prefix[w1] - prefix[w0]) - inter;
        return union > 0 ? inter / (double)union : 0;
    }

    private static double[] BuildSpeeds()
    {
        const double ntsc = 24000.0 / 1001.0;
        double[] ratios = [24 / ntsc, 25 / ntsc, 25.0 / 24];
        var list = new List<double> { 1.0 };
        foreach (double r in ratios)
        {
            list.Add(r);
            list.Add(1 / r);
        }

        return [.. list];
    }

    public sealed record Window(int From, int To, double Shift, double Overlap);

    public sealed record Reference(double Speed, double?[] Truth, IReadOnlyList<Window> Windows);
}
