using System.Globalization;

namespace JebaitedPlayer.Subtitles;

/// <summary>Točka ručnog poravnanja: replika koja u izvornoj datoteci počinje u <paramref name="OriginalMs"/> treba početi u <paramref name="TargetMs"/>.</summary>
public readonly record struct SyncAnchor(double OriginalMs, double TargetMs)
{
    public double OffsetMs => TargetMs - OriginalMs;
}

/// <summary>Rezultat ručnog poravnanja: pomak/brzina ili korekcija po dijelovima (tada je <see cref="Timing"/> bez korekcije).</summary>
public sealed record AnchorSolution(SubtitleTiming Timing, SubtitleTimeMap? Map, string Description);

/// <summary>
/// Ručno poravnanje iz točaka (replika → trenutak kad je čuješ), bez reagiranja u stvarnom vremenu.
/// <list type="bullet">
/// <item>Točke koje se slažu s jednim pomakom (±0,3 s) ili s uobičajenom razlikom u FPS-u daju pomak i brzinu.</item>
/// <item>Inače su titlovi za drugo izdanje: susjedne točke sličnog pomaka čine dio, a granica između dijelova je
/// najdulja pauza u titlovima između njih (prijelaz scene ili čina, gdje se izdanja razlikuju).</item>
/// </list>
/// </summary>
public static class SyncAnchors
{
    private const double ToleranceMs = 300;
    private static readonly CultureInfo Hr = CultureInfo.GetCultureInfo("hr-HR");

    /// <param name="cues">Izvorna vremena replika (za granice između dijelova).</param>
    public static AnchorSolution Solve(IReadOnlyList<SyncAnchor> anchors, IReadOnlyList<(long StartMs, long EndMs)> cues)
    {
        List<SyncAnchor> points = [.. anchors.OrderBy(a => a.OriginalMs)];
        if (points.Count == 0)
        {
            return new AnchorSolution(SubtitleTiming.Identity, null, "Nema točaka.");
        }

        if (points.Count == 1)
        {
            var single = new SubtitleTiming(1, points[0].OffsetMs);
            return new AnchorSolution(single, null, $"Pomak {Seconds(single.DelayMs)}");
        }

        // Jedan pomak ili jedna brzina (najmanji kvadrati) za sve točke.
        (double speed, double delay) = Fit(points);
        double worst = points.Max(p => Math.Abs(p.OriginalMs * speed + delay - p.TargetMs));
        bool nearOne = Math.Abs(speed - 1) <= 0.0015;
        string? preset = SubtitleTiming.FrameRatePresets.FirstOrDefault(p => Math.Abs(p.Speed - speed) < 0.0006).Name;
        if (worst <= ToleranceMs && (nearOne || preset != null))
        {
            if (preset != null)
            {
                speed = SubtitleTiming.FrameRatePresets.First(p => p.Name == preset).Speed;
                delay = points.Average(p => p.TargetMs - p.OriginalMs * speed);
            }
            else if (Math.Abs(speed - 1) < 0.0002)
            {
                speed = 1;
                delay = points.Average(p => p.OffsetMs);
            }

            var linear = new SubtitleTiming(speed, delay);
            string what = Math.Abs(speed - 1) < 1e-9
                ? $"Pomak {Seconds(delay)}"
                : $"Brzina {speed.ToString("0.0000", Hr)}×{(preset != null ? $" ({preset.Replace("Titlovi ", "titlovi za ")})" : string.Empty)}, pomak {Seconds(delay)}";
            return new AnchorSolution(linear, null, what);
        }

        // Po dijelovima: susjedne točke sličnog pomaka čine jedan dio.
        var groups = new List<List<SyncAnchor>> { new() { points[0] } };
        foreach (SyncAnchor p in points.Skip(1))
        {
            if (Math.Abs(p.OffsetMs - groups[^1].Average(a => a.OffsetMs)) <= ToleranceMs)
            {
                groups[^1].Add(p);
            }
            else
            {
                groups.Add([p]);
            }
        }

        var segments = new List<SubtitleTimeMap.Segment>();
        for (int g = 0; g < groups.Count; g++)
        {
            double from = g == 0 ? 0 : LongestPauseBetween(cues, groups[g - 1][^1].OriginalMs, groups[g][0].OriginalMs);
            segments.Add(new SubtitleTimeMap.Segment(from, 1, groups[g].Average(a => a.OffsetMs)));
        }

        var map = new SubtitleTimeMap(segments);
        if (segments.Count == 1)
        {
            return new AnchorSolution(new SubtitleTiming(1, segments[0].OffsetMs), null, $"Pomak {Seconds(segments[0].OffsetMs)}");
        }

        return new AnchorSolution(SubtitleTiming.Identity, map,
            $"Po dijelovima ({segments.Count}): " + string.Join(" / ", segments.Select(s => Seconds(s.OffsetMs))));
    }

    /// <summary>Početak replike nakon najdulje pauze između dva izvorna vremena (granica između dijelova).</summary>
    internal static double LongestPauseBetween(IReadOnlyList<(long StartMs, long EndMs)> cues, double afterMs, double beforeMs)
    {
        double best = -1, at = (afterMs + beforeMs) / 2;
        var sorted = cues.Where(c => c.EndMs > c.StartMs).OrderBy(c => c.StartMs).ToList();
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].StartMs <= afterMs || sorted[i].StartMs > beforeMs)
            {
                continue;
            }

            double gap = sorted[i].StartMs - sorted[i - 1].EndMs;
            if (gap > best)
            {
                best = gap;
                at = sorted[i].StartMs;
            }
        }

        return at;
    }

    private static (double Speed, double Delay) Fit(List<SyncAnchor> points)
    {
        double mx = points.Average(p => p.OriginalMs), my = points.Average(p => p.TargetMs);
        double sxx = points.Sum(p => (p.OriginalMs - mx) * (p.OriginalMs - mx));
        double sxy = points.Sum(p => (p.OriginalMs - mx) * (p.TargetMs - my));
        double speed = sxx > 1e-6 ? Math.Clamp(sxy / sxx, 0.85, 1.15) : 1;
        return (speed, my - speed * mx);
    }

    private static string Seconds(double ms)
    {
        string sign = ms > 0.5 ? "+" : ms < -0.5 ? "−" : string.Empty;
        return $"{sign}{(Math.Abs(ms) / 1000).ToString("0.0", Hr)} s";
    }
}
