namespace JebaitedPlayer.Sync;

/// <summary>Jedna replika titla (sekunde, prema izvornoj datoteci titla).</summary>
public readonly record struct Cue(double Start, double End);

public enum SyncVerdict
{
    /// <summary>Titlovi su već usklađeni s govorom.</summary>
    InSync,

    /// <summary>Pronađen je pomak i/ili brzina s kojom se titlovi poklapaju s govorom.</summary>
    Corrected,

    /// <summary>Titlovi su za drugo izdanje: pomak se mijenja na prijelazima scena, pa je ispravljeno po dijelovima.</summary>
    CorrectedInParts,

    /// <summary>Poklapanje postoji, ali dio filma nije moguće pouzdano izmjeriti.</summary>
    Uncertain,

    /// <summary>Titlovi ne odgovaraju govoru u ovom zvuku (drugi film, druga epizoda…).</summary>
    Mismatch,

    /// <summary>Premalo replika ili govora za pouzdanu provjeru.</summary>
    NotEnoughData,
}

/// <summary>
/// Rezultat poravnanja. Prikazano vrijeme replike je <see cref="Map"/>(izvorno vrijeme): unutar svakog dijela
/// (<see cref="Segments"/>) to je izvorno × brzina + pomak dijela. Jedan dio = isti model kao mpv-ovi <c>sub-speed</c> i <c>sub-delay</c>.
/// </summary>
public sealed record SyncResult
{
    public required SyncVerdict Verdict { get; init; }

    /// <summary>Brzina (zajednička svim dijelovima).</summary>
    public double Speed { get; init; } = 1;

    /// <summary>Pomak prvog dijela (s). Za korekciju po dijelovima vidi <see cref="Segments"/>.</summary>
    public double Offset { get; init; }

    /// <summary>Dijelovi s vlastitim pomakom, poredani po vremenu (jedan = obična korekcija).</summary>
    public IReadOnlyList<SyncSegment> Segments { get; init; } = [];

    /// <summary>Unutarnja ocjena 0–100 (udio pouzdanih dijelova × snaga dokaza). Nije vjerojatnost.</summary>
    public int Confidence { get; init; }

    /// <summary>Udio replika u kojima se nakon poravnanja čuje govor.</summary>
    public double MatchRatio { get; init; }

    /// <summary>Isti udio kad su titlovi namjerno krivo postavljeni (slučajna razina).</summary>
    public double ChanceMatchRatio { get; init; }

    /// <summary>Koliko se vrh korelacije ističe iznad šuma (grubo traženje).</summary>
    public double GlobalScore { get; init; }

    /// <summary>Koliko se konačno poravnanje ističe u odnosu na isto traženje na krivo postavljenom zvuku (z).</summary>
    public double FinalScore { get; init; }

    /// <summary>Prednost ispred najboljeg poravnanja na krivo postavljenom zvuku, po okviru govora (0 = nema razlike).</summary>
    public double Evidence { get; init; }

    /// <summary>Broj dijelova (~3 min) u kojima se poklapanje moglo izmjeriti.</summary>
    public int Windows { get; init; }

    /// <summary>Dijelovi koji se slažu s konačnom korekcijom (±0,25 s).</summary>
    public int AgreeingWindows { get; init; }

    /// <summary>Najveća korekcija na početku ili kraju nekog dijela (s).</summary>
    public double MaxShiftSeconds { get; init; }

    public string Detail { get; init; } = string.Empty;

    /// <summary>Ključ opisa za sučelje (Languages/*.json: "syncDetail.&lt;ključ&gt;"), uz parametre <see cref="DetailArgs"/>.</summary>
    public string DetailKey { get; init; } = string.Empty;

    public object[] DetailArgs { get; init; } = [];

    /// <summary>Rezultat po dijelovima filma (za dijagnostiku i log).</summary>
    public IReadOnlyList<SyncPart> Parts { get; init; } = [];

    public bool IsUsable => Verdict is SyncVerdict.InSync or SyncVerdict.Corrected or SyncVerdict.CorrectedInParts;

    public bool IsPiecewise => Segments.Count > 1;

    /// <summary>Prikazano vrijeme za izvorno vrijeme titla t (s).</summary>
    public double Map(double t)
    {
        if (Segments.Count == 0)
        {
            return t * Speed + Offset;
        }

        SyncSegment segment = Segments[0];
        foreach (SyncSegment s in Segments)
        {
            if (s.From <= t)
            {
                segment = s;
            }
            else
            {
                break;
            }
        }

        return segment.Apply(t);
    }

    /// <summary>Isto što i <see cref="Map"/> (ime iz ranijih verzija).</summary>
    public double Apply(double t) => Map(t);
}

/// <summary>Jedan dio filma: vrijeme u videu, preostali lokalni pomak (null = nema jasnog rezultata), slaže li se.</summary>
public sealed record SyncPart(double Start, double End, double? Residual, double Score, bool Agrees);

public sealed record AlignOptions
{
    /// <summary>Uobičajeni omjeri brzine kad su titlovi rađeni za drugi FPS (23,976 / 24 / 25).</summary>
    public static readonly double[] DefaultSpeeds = BuildSpeeds();

    public double MaxOffsetSeconds { get; init; } = 600;

    public IReadOnlyList<double> Speeds { get; init; } = DefaultSpeeds;

    public double WindowSeconds { get; init; } = 200;

    public int MinCuesPerWindow { get; init; } = 10;

    /// <summary>Duljina dijela za precizno mjerenje unutar dijelova s istim pomakom (s).</summary>
    public double RefineWindowSeconds { get; init; } = 200;

    public int RefineMinCuesPerWindow { get; init; } = 10;

    public double InlierToleranceSeconds { get; init; } = 0.25;

    public double InSyncToleranceSeconds { get; init; } = 0.12;

    /// <summary>Raspon pomaka po dijelovima oko grubog pomaka (s).</summary>
    public double PartsRangeSeconds { get; init; } = 60;

    /// <summary>Kazna za promjenu pomaka između dvije replike (okviri govora od 10 ms).</summary>
    public double SplitPenalty { get; init; } = 400;

    /// <summary>Dodatna kazna po sekundi promjene pomaka: velik skok (npr. 40 s u odjavnoj špici) traži i velik dokaz.</summary>
    public double JumpPenaltyPerSecond { get; init; } = 25;

    /// <summary>Fina korekcija brzine oko standardnih omjera: ± ovoliko (0,005 = 0,5 %).</summary>
    public double FineSpeedSpan { get; init; } = 0.005;

    /// <summary>Najmanja prednost pred krivo postavljenim zvukom (po okviru govora u replikama) da se titlovi prihvate.</summary>
    public double MinEvidence { get; init; } = 0.04;

    /// <summary>Dio je potvrđen kad se poklapanje ističe barem ovoliko (z) u odnosu na krive pomake.</summary>
    public double MinSegmentEvidence { get; init; } = 3;

    /// <summary>Najmanji udio replika u potvrđenim dijelovima da se korekcija primijeni.</summary>
    public double MinConfirmedShare { get; init; } = 0.7;

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
}

/// <summary>
/// Poravnava titlove s govorom u zvuku.
/// <list type="number">
/// <item>Grubo: FFT unakrsna korelacija (50 ms) za svaki uobičajeni omjer brzine, pomak do ±10 min.</item>
/// <item>Po dijelovima (<see cref="PiecewiseAligner"/>): najbolji niz pomaka po replikama uz finu korekciju brzine.
/// Pomak se smije promijeniti samo u pauzi između replika i to plaća kaznu, pa skok mora biti jasno potvrđen govorom.</item>
/// <item>Precizno: lokalni pomaci (10 ms) unutar dijelova daju točnu brzinu i pomak svakog dijela.</item>
/// <item>Provjera: isto traženje na namjerno pomaknutom zvuku daje razinu slučajnog poklapanja; titlovi se prihvaćaju samo
/// kad se jasno ističu iznad nje, a svaki dio mora imati vlastiti dokaz.</item>
/// </list>
/// </summary>
public static class SubtitleAligner
{
    private const int Fps = SpeechTrack.FramesPerSecond;
    private const int CoarseRate = 20;
    private const int CoarseFactor = Fps / CoarseRate;

    public static SyncResult Align(SpeechTrack speech, IReadOnlyList<Cue> rawCues, AlignOptions? options = null)
    {
        options ??= new AlignOptions();
        List<Cue> cues = Normalize(rawCues);
        if (cues.Count < 15)
        {
            return new SyncResult { Verdict = SyncVerdict.NotEnoughData, Detail = $"premalo replika ({cues.Count})", DetailKey = "fewCues", DetailArgs = [cues.Count] };
        }

        if (speech.DurationSeconds < 60 || speech.SpeechRatio < 0.01 || speech.SpeechRatio > 0.99)
        {
            return new SyncResult
            {
                Verdict = SyncVerdict.NotEnoughData,
                Detail = $"premalo zvuka ili govora ({speech.DurationSeconds:0} s, govor {speech.SpeechRatio:P0})",
                DetailKey = "littleSpeech",
                DetailArgs = [speech.DurationSeconds, speech.SpeechRatio],
            };
        }

        // 1) Grubo traženje pomaka za svaki standardni omjer brzine.
        List<(double Speed, double Offset, double Score)> coarse = CoarseSearch(speech, cues, options);
        double mid = (cues[0].Start + cues[^1].End) / 2;

        // 2) Po dijelovima: dva najbolja omjera × fina korekcija brzine (korak 0,1 %), pa doterivanje (0,02 %).
        //    Središnji pomak prati brzinu tako da sredina filma ostane na mjestu.
        (PiecewiseAligner.Solution Solution, double Center) best = default;
        bool any = false;
        void Try(double speed, double center)
        {
            PiecewiseAligner.Solution s = PiecewiseAligner.Solve(speech, cues, speed, center, options.PartsRangeSeconds, 0.1,
                options.SplitPenalty, options.JumpPenaltyPerSecond, backtrack: false);
            if (!any || s.Score > best.Solution!.Score)
            {
                best = (s, center);
                any = true;
            }
        }

        foreach ((double k, double d, _) in coarse.Take(2))
        {
            for (double f = -options.FineSpeedSpan; f <= options.FineSpeedSpan + 1e-12; f += 0.001)
            {
                Try(k * (1 + f), d - k * f * mid);
            }
        }

        (double k0, double c0) = (best.Solution!.Speed, best.Center);
        for (double g = -0.0008; g <= 0.0008 + 1e-12; g += 0.0002)
        {
            if (Math.Abs(g) > 1e-12)
            {
                Try(k0 * (1 + g), c0 - k0 * g * mid);
            }
        }

        double searchScore = best.Solution!.Score;
        double speedFound = best.Solution.Speed;
        double centerFound = best.Center;

        // 3) Isto traženje na namjerno pomaknutom zvuku: razina slučajnog poklapanja.
        var nulls = new List<double>();
        foreach (double fraction in (double[])[0.23, 0.38, 0.52, 0.67, 0.81])
        {
            SpeechTrack rotated = speech.Rotate((int)(speech.Length * fraction));
            nulls.Add(PiecewiseAligner.Solve(rotated, cues, speedFound, centerFound, options.PartsRangeSeconds, 0.1,
                options.SplitPenalty, options.JumpPenaltyPerSecond, backtrack: false).Score);
        }

        double spokenFrames = cues.Sum(c => Math.Max(1, (SpokenEnd(c) - c.Start) * speedFound * Fps));
        (double nullMean, double nullStd) = MeanStd(nulls);
        double evidence = (searchScore - nulls.Max()) / spokenFrames;
        double finalScore = nullStd > 1e-9 ? (searchScore - nullMean) / nullStd : 0;

        // 4) Konačni niz pomaka (korak 50 ms), dijelovi, precizno doterivanje brzine i pomaka (10 ms).
        PiecewiseAligner.Solution final = PiecewiseAligner.Solve(speech, cues, speedFound, centerFound, options.PartsRangeSeconds,
            0.05, options.SplitPenalty, options.JumpPenaltyPerSecond);
        List<Part> parts = BuildParts(cues, final.Offsets, final.Speed);
        for (int pass = 0; pass < 2; pass++)
        {
            Refine(speech, cues, parts, pass == 0 ? 1.5 : 1.0, options, ownSpeeds: pass == 1);
            parts = MergeParts(cues, parts);
        }

        // Brzina vrlo blizu standardnog omjera (razlika kroz cijeli dio < 0,15 s) je šum traženja: postavi je točno na omjer
        // (sredina dijela ostaje na mjestu) i još jednom izmjeri samo pomake.
        if (SnapSpeeds(cues, parts, options))
        {
            Refine(speech, cues, parts, 1.0, options, ownSpeeds: false, allowSlope: false);
            parts = MergeParts(cues, parts);
        }

        // Zajednička brzina: ona dijela s najviše replika (dijelovi s izmjerenim vlastitim klizanjem imaju svoju).
        double speed = parts.OrderByDescending(p => p.To - p.From).First().Speed;

        // 5) Dokaz za svaki dio. Dio bez jasnog dokaza (npr. glazba preko dijaloga) ne zadržava nesiguran vlastiti pomak,
        //    nego preuzme pomak potvrđenog susjeda koji mu bolje odgovara (najčešće je to stvarno isti dio).
        var evidenceOf = parts.Select(p => SegmentEvidence(speech, cues, p.From, p.To, p.Speed, p.Offset)).ToList();
        bool IsConfirmed(int i) => evidenceOf[i] >= options.MinSegmentEvidence && parts[i].To - parts[i].From >= 5;
        int confirmedCues = Enumerable.Range(0, parts.Count).Where(IsConfirmed).Sum(i => parts[i].To - parts[i].From);
        for (int i = 0; i < parts.Count; i++)
        {
            if (IsConfirmed(i))
            {
                continue;
            }

            Part p = parts[i];

            // Susjedov pomak mora ovaj dio objašnjavati gotovo jednako dobro kao vlastiti (inače je vlastiti vjerojatno točan,
            // samo s manje dokaza, npr. početak epizode s malo dijaloga).
            double own = BalancedScore(speech, cues, p.From, p.To, p.Speed, p.Offset, 0, out _);
            (double Score, Part? From) adopt = (own - 0.05, null);
            foreach (int ni in new[] { i - 1, i + 1 })
            {
                if (ni >= 0 && ni < parts.Count && IsConfirmed(ni))
                {
                    Part n = parts[ni];
                    double score = BalancedScore(speech, cues, p.From, p.To, n.Speed, n.Offset, 0, out _);
                    if (score > adopt.Score)
                    {
                        adopt = (score, n);
                    }
                }
            }

            if (adopt.From is { } neighbour)
            {
                parts[i] = p with { Offset = neighbour.Offset, Speed = neighbour.Speed };
            }
        }

        // Spoji dijelove koji sad imaju isti pomak (dokaz spojenog dijela je najjači od njih).
        for (int i = parts.Count - 1; i > 0; i--)
        {
            if (Math.Abs(parts[i].Offset - parts[i - 1].Offset) < 1e-9 && Math.Abs(parts[i].Speed - parts[i - 1].Speed) < 1e-12)
            {
                parts[i - 1] = parts[i - 1] with { To = parts[i].To };
                evidenceOf[i - 1] = Math.Max(evidenceOf[i - 1], evidenceOf[i]);
                parts.RemoveAt(i);
                evidenceOf.RemoveAt(i);
            }
        }

        // 6) Provjera po manjim dijelovima (~3 min) s konačnom korekcijom.
        var segments = new List<SyncSegment>();
        var checks = new List<SyncPart>();
        double[] offsets = new double[cues.Count];
        double[] speeds = new double[cues.Count];
        for (int pi = 0; pi < parts.Count; pi++)
        {
            Part p = parts[pi];
            double z = evidenceOf[pi];
            bool confirmed = IsConfirmed(pi);
            segments.Add(new SyncSegment(p.From == 0 ? 0 : cues[p.From].Start, cues[p.To - 1].End, p.Speed, p.Offset, p.To - p.From,
                Math.Round(z, 1), confirmed));
            for (int i = p.From; i < p.To; i++)
            {
                offsets[i] = p.Offset;
                speeds[i] = p.Speed;
            }

            List<Cue> sub = cues.GetRange(p.From, p.To - p.From);
            foreach (WindowMeasure w in MeasureWindows(speech, sub, p.Speed, p.Offset, 3, options))
            {
                checks.Add(new SyncPart(w.Start, w.End, double.IsNaN(w.Delta) ? null : w.Delta, w.Weight,
                    !double.IsNaN(w.Delta) && Math.Abs(w.Delta) <= options.InlierToleranceSeconds));
            }
        }

        double confirmedShare = confirmedCues / (double)cues.Count;
        int agreeing = checks.Count(c => c.Agrees);
        double matchRatio = MatchRatio(speech, cues, speeds, offsets, 0);
        double chance = (MatchRatio(speech, cues, speeds, offsets, 97 * Fps) + MatchRatio(speech, cues, speeds, offsets, -131 * Fps)
            + MatchRatio(speech, cues, speeds, offsets, 211 * Fps)) / 3;
        double maxShift = segments.Max(s => Math.Max(Math.Abs(s.Apply(s.From) - s.From), Math.Abs(s.Apply(s.To) - s.To)));
        int confidence = (int)Math.Round(100 * confirmedShare * Math.Clamp(evidence / (2 * options.MinEvidence), 0, 1));

        SyncVerdict verdict;
        string detail;
        string detailKey;
        object[] detailArgs = [];
        if (evidence < options.MinEvidence)
        {
            verdict = SyncVerdict.Mismatch;
            detail = "replike se ne poklapaju s govorom";
            detailKey = "mismatch";
            confidence = 0;
        }
        else if (confirmedShare < options.MinConfirmedShare)
        {
            verdict = SyncVerdict.Uncertain;
            detail = segments.Count > 1
                ? $"titlovi za drugo izdanje, ali pouzdano je izmjereno samo {confirmedShare:P0} replika"
                : $"poklapanje je preslabo (pouzdano {confirmedShare:P0} replika)";
            detailKey = segments.Count > 1 ? "uncertainParts" : "weak";
            detailArgs = [confirmedShare];
        }
        else if (maxShift <= options.InSyncToleranceSeconds)
        {
            verdict = SyncVerdict.InSync;
            detail = $"poklapa se {agreeing} od {checks.Count} dijelova";
            detailKey = "agree";
            detailArgs = [agreeing, checks.Count];
        }
        else if (segments.Count == 1)
        {
            verdict = SyncVerdict.Corrected;
            detail = $"poklapa se {agreeing} od {checks.Count} dijelova";
            detailKey = "agree";
            detailArgs = [agreeing, checks.Count];
        }
        else
        {
            verdict = SyncVerdict.CorrectedInParts;
            detail = $"titlovi za drugo izdanje: {segments.Count} dijelova, pomak od {segments.Min(s => s.Offset):+0.0;-0.0} do " +
                     $"{segments.Max(s => s.Offset):+0.0;-0.0} s (poklapa se {agreeing} od {checks.Count} dijelova)";
            detailKey = "parts";
            detailArgs = [segments.Count, segments.Min(s => s.Offset), segments.Max(s => s.Offset), agreeing, checks.Count];
        }

        return new SyncResult
        {
            Verdict = verdict,
            Speed = speed,
            Offset = segments[0].Offset,
            Segments = segments,
            Confidence = confidence,
            MatchRatio = matchRatio,
            ChanceMatchRatio = chance,
            GlobalScore = coarse[0].Score,
            FinalScore = finalScore,
            Evidence = evidence,
            Windows = checks.Count,
            AgreeingWindows = agreeing,
            Parts = checks,
            MaxShiftSeconds = maxShift,
            Detail = detail,
            DetailKey = detailKey,
            DetailArgs = detailArgs,
        };
    }

    /// <summary>Sortira, odbacuje neispravne i spaja preklopljene replike.</summary>
    internal static List<Cue> Normalize(IReadOnlyList<Cue> cues)
    {
        var sorted = cues.Where(c => c.End > c.Start && c.Start >= 0 && double.IsFinite(c.End))
            .OrderBy(c => c.Start).ToList();
        var merged = new List<Cue>(sorted.Count);
        foreach (Cue c in sorted)
        {
            if (merged.Count > 0 && c.Start <= merged[^1].End)
            {
                merged[^1] = merged[^1] with { End = Math.Max(merged[^1].End, c.End) };
            }
            else
            {
                merged.Add(c);
            }
        }

        return merged;
    }

    /// <summary>
    /// Replika obično počinje s govorom, ali ostaje na ekranu još malo radi čitanja. Taj "rep" ne očekujemo kao govor,
    /// inače bi poravnanje sustavno pomicalo titlove ranije.
    /// </summary>
    internal static double SpokenEnd(Cue c) => c.End - Math.Min(0.35, 0.25 * (c.End - c.Start));

    /// <summary>Dio titlova [From, To) (indeksi replika) s istim pomakom i brzinom.</summary>
    private sealed record Part(int From, int To, double Offset, double Speed);

    private static List<Part> BuildParts(List<Cue> cues, double[] offsets, double speed)
    {
        var parts = new List<Part>();
        int start = 0;
        for (int i = 1; i <= cues.Count; i++)
        {
            if (i == cues.Count || Math.Abs(offsets[i] - offsets[start]) > 1e-9)
            {
                parts.Add(new Part(start, i, offsets[start], speed));
                start = i;
            }
        }

        return MergeParts(cues, parts);
    }

    /// <summary>
    /// Spaja susjedne dijelove gotovo istog pomaka (&lt; 0,2 s) i premale dijelove (&lt; 5 replika ili &lt; 20 s)
    /// pripaja susjedu sličnijeg pomaka.
    /// </summary>
    private static List<Part> MergeParts(List<Cue> cues, List<Part> parts)
    {
        bool changed = true;
        while (changed && parts.Count > 1)
        {
            changed = false;
            for (int i = 0; i + 1 < parts.Count; i++)
            {
                // Gotovo isti prikaz na granici dijelova (uz brzinu svakog dijela).
                Part a = parts[i], b = parts[i + 1];
                double boundary = cues[b.From].Start;
                bool sameSpeed = Math.Abs(a.Speed - b.Speed) < 1e-4;
                if (sameSpeed && Math.Abs(a.Offset - b.Offset) < 0.2
                    || !sameSpeed && Math.Abs(a.Speed - b.Speed) < 0.002
                       && Math.Abs(boundary * a.Speed + a.Offset - (boundary * b.Speed + b.Offset)) < 0.2)
                {
                    int na = a.To - a.From, nb = b.To - b.From;
                    Part big = na >= nb ? a : b;
                    parts[i] = new Part(a.From, b.To, sameSpeed ? (a.Offset * na + b.Offset * nb) / (na + nb) : big.Offset, big.Speed);
                    parts.RemoveAt(i + 1);
                    changed = true;
                    break;
                }
            }

            if (changed)
            {
                continue;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                Part p = parts[i];
                double duration = cues[p.To - 1].End - cues[p.From].Start;
                if (p.To - p.From >= 5 && duration >= 20)
                {
                    continue;
                }

                // Pripoji susjedu čiji je pomak bliži (rubni dio ima samo jednog susjeda).
                int target = i == 0 ? 1
                    : i == parts.Count - 1 ? i - 1
                    : Math.Abs(parts[i - 1].Offset - p.Offset) <= Math.Abs(parts[i + 1].Offset - p.Offset) ? i - 1 : i + 1;
                Part t = parts[target];
                parts[Math.Min(i, target)] = new Part(Math.Min(p.From, t.From), Math.Max(p.To, t.To), t.Offset, t.Speed);
                parts.RemoveAt(Math.Max(i, target));
                changed = true;
                break;
            }
        }

        return parts;
    }

    /// <summary>
    /// Precizno: lokalni pomaci (10 ms) po manjim dijelovima (~3 min) unutar svakog dijela daju zajednički nagib (brzinu)
    /// i pomak svakog dijela. Uz <paramref name="ownSpeeds"/> dio s jasno izmjerenim vlastitim klizanjem dobije i svoju brzinu
    /// (npr. titlovi kojima u drugoj polovici filma vrijeme polako bježi). Dijelovi se ažuriraju u <paramref name="parts"/>.
    /// </summary>
    private static bool SnapSpeeds(List<Cue> cues, List<Part> parts, AlignOptions options)
    {
        bool snapped = false;
        for (int i = 0; i < parts.Count; i++)
        {
            Part p = parts[i];
            double from = cues[p.From].Start, to = cues[p.To - 1].End, mid = (from + to) / 2;
            double target = options.Speeds.OrderBy(k => Math.Abs(k - p.Speed)).First();
            if (Math.Abs(target - p.Speed) > 1e-12 && Math.Abs(target - p.Speed) * (to - from) < 0.15)
            {
                parts[i] = p with { Speed = target, Offset = p.Offset + (p.Speed - target) * mid };
                snapped = true;
            }
        }

        return snapped;
    }

    private static void Refine(SpeechTrack speech, List<Cue> cues, List<Part> parts, double range, AlignOptions options, bool ownSpeeds,
        bool allowSlope = true)
    {
        // Manji dijelovi (~100 s) da se i u činu od nekoliko minuta izmjeri klizanje.
        AlignOptions fine = options with { WindowSeconds = options.RefineWindowSeconds, MinCuesPerWindow = options.RefineMinCuesPerWindow };
        var measures = new List<(int Part, WindowMeasure W)>();
        for (int p = 0; p < parts.Count; p++)
        {
            List<Cue> sub = cues.GetRange(parts[p].From, parts[p].To - parts[p].From);
            foreach (WindowMeasure w in MeasureWindows(speech, sub, parts[p].Speed, parts[p].Offset, range, fine))
            {
                if (!double.IsNaN(w.Delta))
                {
                    measures.Add((p, w));
                }
            }
        }

        if (measures.Count == 0)
        {
            return;
        }

        // Robusno: odbaci mjerenja daleko od medijana svog dijela (npr. kratka scena koja ne pripada dijelu).
        var kept = measures.Where(m =>
        {
            double median = Median(measures.Where(x => x.Part == m.Part).Select(x => x.W.Delta));
            return Math.Abs(m.W.Delta - median) <= Math.Max(0.5, range / 2);
        }).ToList();
        var groups = kept.GroupBy(m => m.Part).ToDictionary(g => g.Key, g => g.Select(m => m.W).ToList());

        // Zajednički nagib β uz zaseban odsječak po dijelu (vagani najmanji kvadrati unutar dijelova).
        double sxx = 0, sxy = 0, span = 0;
        foreach ((int p, List<WindowMeasure> ws) in groups)
        {
            (double t, double d) = WeightedMean(ws);
            foreach (WindowMeasure w in ws)
            {
                sxx += w.Weight * (w.Center - t) * (w.Center - t);
                sxy += w.Weight * (w.Center - t) * (w.Delta - d);
            }

            span += ws.Max(w => w.Center) - ws.Min(w => w.Center);
        }

        double beta = sxx > 1e-9 && kept.Count >= 3 ? sxy / sxx : 0;

        // Drift se prihvaća samo kad je stvaran: kroz izmjereni dio mora narasti barem 0,1 s i ne smije biti nerealan (> 0,5 %).
        if (!allowSlope || Math.Abs(beta) > 0.005 || Math.Abs(beta) * span < 0.1 || span < 300)
        {
            beta = 0;
        }

        for (int p = 0; p < parts.Count; p++)
        {
            double b = beta;
            double alpha = 0;
            if (groups.TryGetValue(p, out List<WindowMeasure>? ws))
            {
                (double t, double d) = WeightedMean(ws);
                if (ownSpeeds && ws.Count >= 3)
                {
                    // Vlastito klizanje dijela: prihvaća se kad je izmjerljivo (≥ 0,25 s kroz dio) i realno.
                    double own = Slope(ws, t, d);
                    double ownSpan = ws.Max(w => w.Center) - ws.Min(w => w.Center);
                    if (ownSpan >= 300 && Math.Abs(own) <= 0.005 && Math.Abs(own - beta) * ownSpan >= 0.25)
                    {
                        b = own;
                    }
                }

                alpha = d - b * t;
            }

            Part part = parts[p];
            parts[p] = part with { Offset = (1 + b) * part.Offset + alpha, Speed = part.Speed * (1 + b) };
        }
    }

    private static (double T, double D) WeightedMean(List<WindowMeasure> ws)
    {
        double sw = ws.Sum(w => w.Weight);
        return (ws.Sum(w => w.Weight * w.Center) / sw, ws.Sum(w => w.Weight * w.Delta) / sw);
    }

    private static double Slope(List<WindowMeasure> ws, double t, double d)
    {
        double sxx = 0, sxy = 0;
        foreach (WindowMeasure w in ws)
        {
            sxx += w.Weight * (w.Center - t) * (w.Center - t);
            sxy += w.Weight * (w.Center - t) * (w.Delta - d);
        }

        return sxx > 1e-9 ? sxy / sxx : 0;
    }

    /// <summary>Koliko se poklapanje dijela na njegovom pomaku ističe u odnosu na pomake ±1–30 s (z).</summary>
    private static double SegmentEvidence(SpeechTrack speech, List<Cue> cues, int from, int to, double speed, double offset)
    {
        double at = BalancedScore(speech, cues, from, to, speed, offset, 0, out _);
        var others = new List<double>();
        for (double s = 1.0; s <= 30; s += 0.1)
        {
            others.Add(BalancedScore(speech, cues, from, to, speed, offset, (int)Math.Round(s * Fps), out _));
            others.Add(BalancedScore(speech, cues, from, to, speed, offset, (int)Math.Round(-s * Fps), out _));
        }

        (double mean, double std) = MeanStd(others);
        return std > 1e-9 ? (at - mean) / std : 0;
    }

    /// <summary>Grubo traženje za svaki standardni omjer brzine; poredano od najboljeg.</summary>
    private static List<(double Speed, double Offset, double Score)> CoarseSearch(SpeechTrack speech, List<Cue> cues, AlignOptions options)
    {
        int audioLength = (speech.Length + CoarseFactor - 1) / CoarseFactor;
        var a = new double[audioLength];
        ReadOnlySpan<byte> frames = speech.Frames;
        for (int i = 0; i < frames.Length; i++)
        {
            a[i / CoarseFactor] += frames[i];
        }

        SubtractMean(a);

        int maxLag = (int)(options.MaxOffsetSeconds * CoarseRate);
        double maxSpeed = options.Speeds.Max();
        int subLength = (int)Math.Ceiling(cues[^1].End * maxSpeed * CoarseRate) + 2;
        int n = Fft.NextPowerOfTwo(Math.Max(audioLength, subLength) + maxLag + 1);
        var spectrum = Fft.Spectrum(a, n);

        var results = new List<(double Speed, double Offset, double Score)>();
        int exclude = 3 * CoarseRate;
        foreach (double k in options.Speeds)
        {
            double[] s = Coverage(cues, k, CoarseRate);
            SubtractMean(s);
            double[] c = Fft.CrossCorrelate(spectrum, s, maxLag);

            int peak = 0;
            for (int i = 1; i < c.Length; i++)
            {
                if (c[i] > c[peak])
                {
                    peak = i;
                }
            }

            double sum = 0, sumSq = 0;
            int count = 0;
            for (int i = 0; i < c.Length; i++)
            {
                if (Math.Abs(i - peak) > exclude)
                {
                    sum += c[i];
                    sumSq += c[i] * c[i];
                    count++;
                }
            }

            double mean = sum / Math.Max(1, count);
            double std = Math.Sqrt(Math.Max(0, sumSq / Math.Max(1, count) - mean * mean));
            double z = std > 1e-12 ? (c[peak] - mean) / std : 0;
            results.Add((k, (peak - maxLag) / (double)CoarseRate, z));
        }

        return [.. results.OrderByDescending(r => r.Score)];
    }

    /// <summary>Pokrivenost titlovima po odsječcima (0–1) uz zadanu brzinu, od vremena 0.</summary>
    private static double[] Coverage(List<Cue> cues, double speed, int rate)
    {
        var s = new double[(int)Math.Ceiling(cues[^1].End * speed * rate) + 2];
        foreach (Cue c in cues)
        {
            double from = c.Start * speed * rate, to = SpokenEnd(c) * speed * rate;
            int i0 = (int)from, i1 = (int)to;
            if (i0 == i1)
            {
                s[i0] += to - from;
                continue;
            }

            s[i0] += i0 + 1 - from;
            for (int i = i0 + 1; i < i1; i++)
            {
                s[i] += 1;
            }

            s[i1] += to - i1;
        }

        return s;
    }

    private readonly record struct WindowMeasure(double Center, double Delta, double Weight, double Start, double End);

    private static List<WindowMeasure> MeasureWindows(SpeechTrack speech, List<Cue> cues, double speed, double offset,
        double range, AlignOptions options)
    {
        var result = new List<WindowMeasure>();
        int maxShift = (int)Math.Round(range * Fps);
        int start = 0;
        while (start < cues.Count)
        {
            int end = start;
            while (end < cues.Count && (end - start < options.MinCuesPerWindow || cues[end].Start - cues[start].Start < options.WindowSeconds / speed))
            {
                end++;
            }

            // Zadnji ostatak pripaja se prethodnom dijelu ako je premalen.
            if (cues.Count - end < options.MinCuesPerWindow)
            {
                end = cues.Count;
            }

            var scores = new double[2 * maxShift + 1];
            for (int d = -maxShift; d <= maxShift; d++)
            {
                scores[d + maxShift] = BalancedScore(speech, cues, start, end, speed, offset, d, out _);
            }

            int peak = 0;
            for (int i = 1; i < scores.Length; i++)
            {
                if (scores[i] > scores[peak])
                {
                    peak = i;
                }
            }

            var others = new List<double>(scores.Length);
            for (int i = 0; i < scores.Length; i++)
            {
                if (Math.Abs(i - peak) > Fps / 2)
                {
                    others.Add(scores[i]);
                }
            }

            (double mean, double std) = MeanStd(others);
            double z = std > 1e-9 ? (scores[peak] - mean) / std : 0;

            // Vrh na rubu raspona znači da je pravi pomak izvan njega: takav dio ne mjerimo.
            bool atEdge = peak == 0 || peak == scores.Length - 1;
            double from = cues[start].Start * speed + offset, to = cues[end - 1].End * speed + offset;
            if (scores[peak] > 0.08 && z >= 3 && !atEdge)
            {
                result.Add(new WindowMeasure((from + to) / 2, (peak - maxShift) / (double)Fps, scores[peak], from, to));
            }
            else
            {
                result.Add(new WindowMeasure(double.NaN, double.NaN, Math.Max(0, scores[peak]), from, to));
            }

            start = end;
        }

        return result;
    }

    /// <summary>
    /// Udio govora unutar replika minus udio govora između njih (u istom rasponu), uz pomak od <paramref name="shift"/> okvira.
    /// Neovisno o tome koliko film ima govora: 0 = nema veze, ~0,4–0,7 = jasno poklapanje.
    /// </summary>
    private static double BalancedScore(SpeechTrack speech, List<Cue> cues, int from, int to, double speed, double offset,
        int shift, out double inCueRatio)
    {
        long inCue = 0, cueFrames = 0;
        for (int i = from; i < to; i++)
        {
            int s = Frame(cues[i].Start * speed + offset) + shift;
            int e = Frame(SpokenEnd(cues[i]) * speed + offset) + shift;
            inCue += speech.Prefix(e) - speech.Prefix(s);
            cueFrames += e - s;
        }

        int w0 = Frame(cues[from].Start * speed + offset) - 2 * Fps + shift;
        int w1 = Frame(cues[to - 1].End * speed + offset) + 2 * Fps + shift;
        long windowSpeech = speech.Prefix(w1) - speech.Prefix(w0);
        long nonCue = (w1 - w0) - cueFrames;
        inCueRatio = cueFrames > 0 ? inCue / (double)cueFrames : 0;
        if (cueFrames <= 0 || nonCue <= 0)
        {
            return 0;
        }

        return inCueRatio - (windowSpeech - inCue) / (double)nonCue;
    }

    private static double MatchRatio(SpeechTrack speech, List<Cue> cues, double[] speeds, double[] offsets, int shift)
    {
        int counted = 0, hits = 0;
        for (int i = 0; i < cues.Count; i++)
        {
            Cue c = cues[i];
            if (c.End - c.Start < 0.3)
            {
                continue;
            }

            int s = Frame(c.Start * speeds[i] + offsets[i]) + shift;
            int e = Frame(c.End * speeds[i] + offsets[i]) + shift;
            counted++;
            if (speech.Prefix(e) - speech.Prefix(s) >= 0.3 * (e - s))
            {
                hits++;
            }
        }

        return counted == 0 ? 0 : hits / (double)counted;
    }

    private static int Frame(double seconds) => (int)Math.Round(seconds * Fps);

    private static void SubtractMean(double[] x)
    {
        double mean = x.Average();
        for (int i = 0; i < x.Length; i++)
        {
            x[i] -= mean;
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] s = [.. values.Order()];
        return s.Length == 0 ? 0 : s[s.Length / 2];
    }

    private static (double Mean, double Std) MeanStd(List<double> x)
    {
        if (x.Count == 0)
        {
            return (0, 0);
        }

        double mean = x.Average();
        double variance = x.Sum(v => (v - mean) * (v - mean)) / x.Count;
        return (mean, Math.Sqrt(variance));
    }
}
