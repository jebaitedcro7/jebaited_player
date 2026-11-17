namespace JebaitedPlayer.Sync;

/// <summary>
/// Dio titlova s vlastitim pomakom: za izvorno vrijeme t u [<see cref="From"/>, sljedeći dio) prikazano vrijeme je
/// t × <see cref="Speed"/> + <see cref="Offset"/>.
/// </summary>
/// <param name="From">Početak dijela u izvornom vremenu titla (s); prvi dio vrijedi i za sve prije njega.</param>
/// <param name="To">Kraj zadnje replike dijela u izvornom vremenu (s).</param>
/// <param name="Cues">Broj replika u dijelu.</param>
/// <param name="Evidence">Koliko se poklapanje ističe u odnosu na krivo postavljene pomake (z-vrijednost).</param>
/// <param name="Confirmed">Dio je pouzdano izmjeren (dovoljno replika i jasan dokaz).</param>
public sealed record SyncSegment(double From, double To, double Speed, double Offset, int Cues, double Evidence, bool Confirmed)
{
    public double Apply(double t) => t * Speed + Offset;
}

/// <summary>
/// Poravnanje po dijelovima: titlovi rađeni za drugo izdanje (TV, DVD, BluRay) često imaju isti tekst, ali na prijelazima
/// scena (reklame, kraj čina) pomak skoči za nekoliko sekundi. Jedan pomak i jedna brzina tada ne mogu biti točni cijelim
/// filmom. Dinamičko programiranje traži najbolji niz pomaka po replikama; promjena pomaka dopuštena je samo u pauzi
/// između replika i plaća kaznu, pa se skok prihvaća samo kad ga govor jasno potvrđuje.
/// </summary>
internal static class PiecewiseAligner
{
    private const int Fps = SpeechTrack.FramesPerSecond;

    internal sealed record Solution(double Speed, double[] Offsets, double Score, int Splits);

    /// <summary>
    /// Najbolji niz pomaka (po replici) za zadanu brzinu, u rasponu <paramref name="center"/> ± <paramref name="range"/> s,
    /// korakom <paramref name="step"/> s. Rezultat: pomak svake replike, ukupna ocjena (okviri govora iznad očekivanog) i broj skokova.
    /// </summary>
    /// <param name="penalty">Stalna kazna za svaku promjenu pomaka.</param>
    /// <param name="jumpCost">Dodatna kazna po sekundi promjene: veliki skok (npr. 40 s u odjavnoj špici) mora imati i velik dokaz.</param>
    public static Solution Solve(SpeechTrack speech, IReadOnlyList<Cue> cues, double speed, double center, double range, double step,
        double penalty, double jumpCost, double minSplitGap = 0.25, bool backtrack = true)
    {
        int n = cues.Count;
        int half = (int)Math.Round(range / step);
        int states = 2 * half + 1;
        int stepFrames = Math.Max(1, (int)Math.Round(step * Fps));
        int baseShift = (int)Math.Round(center * Fps) - half * stepFrames;

        // Okviri (bez pomaka) i lokalna gustoća govora oko svake replike (±60 s, uz središnji pomak).
        var a = new int[n];
        var b = new int[n];
        var end = new int[n];
        var density = new double[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = Frame(cues[i].Start * speed);
            b[i] = Math.Max(a[i] + 1, Frame(SubtitleAligner.SpokenEnd(cues[i]) * speed));
            end[i] = Math.Max(b[i], Frame(cues[i].End * speed));
            int mid = (a[i] + end[i]) / 2 + (int)Math.Round(center * Fps);
            int lo = Math.Max(0, mid - 60 * Fps), hi = Math.Min(speech.Length, mid + 60 * Fps);
            density[i] = hi > lo ? (speech.Prefix(hi) - speech.Prefix(lo)) / (double)(hi - lo) : speech.SpeechRatio;
        }

        var prev = new double[states];
        var cur = new double[states];
        var up = new double[states];
        var upAt = new int[states];
        var down = new double[states];
        var downAt = new int[states];
        var window = new int[states];
        double stepCost = jumpCost * step;
        int[][]? from = backtrack ? new int[n][] : null;
        var splitsAt = new int[states];
        var splitsCur = new int[states];

        for (int j = 0; j < states; j++)
        {
            int shift = baseShift + j * stepFrames;
            prev[j] = CueScore(speech, a[0] + shift, b[0] + shift, density[0]);
        }

        if (from != null)
        {
            from[0] = new int[states];
            for (int j = 0; j < states; j++)
            {
                from[0][j] = j;
            }
        }

        for (int i = 1; i < n; i++)
        {
            double gapSeconds = cues[i].Start - cues[i - 1].End;
            bool canSplit = gapSeconds >= minSplitGap;

            // Pomak smije pasti najviše za duljinu pauze (replike ne smiju zamijeniti mjesta).
            int maxDrop = canSplit ? (int)Math.Floor(gapSeconds * speed / step) : 0;
            bool scoreGap = gapSeconds is > 0.05 and <= 5;

            // Rezovi između izdanja su na prijelazima scena, a tamo su pauze dulje: skok u kratkoj pauzi (0,25 s) stoji
            // 1,5 kazne, u pauzi od 4 s 0,6, a u vrlo dugoj (kraj čina, ≥ 30 s) 0,3 kazne. Tako granica dijela ne
            // "prepuže" u susjednu kraću pauzu kad govor oko granice ne daje jasan dokaz.
            double gapFactor = gapSeconds <= 4
                ? Math.Clamp(1.6 - 0.25 * gapSeconds, 0.6, 1.5)
                : Math.Max(0.3, 0.6 - 0.3 * Math.Log(gapSeconds / 4) / Math.Log(30.0 / 4));
            if (canSplit)
            {
                // Skok s nižeg pomaka (j' ≤ j): up[j] = max(prev[j'] − trošak·(j − j')).
                up[0] = prev[0];
                upAt[0] = 0;
                for (int j = 1; j < states; j++)
                {
                    double carried = up[j - 1] - stepCost;
                    if (prev[j] >= carried)
                    {
                        up[j] = prev[j];
                        upAt[j] = j;
                    }
                    else
                    {
                        up[j] = carried;
                        upAt[j] = upAt[j - 1];
                    }
                }

                // Skok s višeg pomaka (j < j' ≤ j + maxDrop): klizni maksimum od prev[j'] − trošak·j', pa + trošak·j.
                int head = 0, tail = 0;
                for (int j = states - 1; j >= 0; j--)
                {
                    int add = j + 1;
                    if (add < states && maxDrop > 0)
                    {
                        double value = prev[add] - stepCost * add;
                        while (tail > head && prev[window[tail - 1]] - stepCost * window[tail - 1] <= value)
                        {
                            tail--;
                        }

                        window[tail++] = add;
                    }

                    while (tail > head && window[head] > j + maxDrop)
                    {
                        head++;
                    }

                    if (tail > head)
                    {
                        int k = window[head];
                        down[j] = prev[k] - stepCost * (k - j);
                        downAt[j] = k;
                    }
                    else
                    {
                        down[j] = double.NegativeInfinity;
                        downAt[j] = j;
                    }
                }
            }
            int[]? row = from != null ? from[i] = new int[states] : null;
            for (int j = 0; j < states; j++)
            {
                int shift = baseShift + j * stepFrames;
                double stay = prev[j];
                if (scoreGap)
                {
                    // Pauza između dvije replike s istim pomakom: govor u njoj je dokaz protiv tog pomaka.
                    stay -= CueScore(speech, end[i - 1] + shift, a[i] + shift, density[i]);
                }

                double value = stay;
                int source = j;
                int splits = splitsAt[j];
                if (canSplit)
                {
                    (double jump, int at) = up[j] >= down[j] ? (up[j], upAt[j]) : (down[j], downAt[j]);
                    jump -= penalty * gapFactor;
                    if (jump > value)
                    {
                        value = jump;
                        source = at;
                        splits = splitsAt[source] + 1;
                    }
                }

                cur[j] = value + CueScore(speech, a[i] + shift, b[i] + shift, density[i]);
                splitsCur[j] = splits;
                if (row != null)
                {
                    row[j] = source;
                }
            }

            (prev, cur) = (cur, prev);
            (splitsAt, splitsCur) = (splitsCur, splitsAt);
        }

        int last = 0;
        for (int j = 1; j < states; j++)
        {
            if (prev[j] > prev[last])
            {
                last = j;
            }
        }

        var offsets = new double[n];
        if (from != null)
        {
            int j = last;
            for (int i = n - 1; i >= 0; i--)
            {
                offsets[i] = (baseShift + j * stepFrames) / (double)Fps;
                j = from[i][j];
            }
        }
        else
        {
            Array.Fill(offsets, (baseShift + last * stepFrames) / (double)Fps);
        }

        return new Solution(speed, offsets, prev[last], splitsAt[last]);
    }

    /// <summary>Govor unutar raspona okvira iznad očekivanog prema lokalnoj gustoći (može biti negativno).</summary>
    private static double CueScore(SpeechTrack speech, int from, int to, double density) =>
        to <= from ? 0 : speech.Prefix(to) - speech.Prefix(from) - density * (to - from);

    private static int Frame(double seconds) => (int)Math.Round(seconds * Fps);
}
