namespace JebaitedPlayer.Subtitles;

/// <summary>
/// Linearna korekcija vremena titlova: <c>prikaz = original × Speed + DelayMs</c>.
/// <para>
/// Isti model koristi mpv (<c>sub-speed</c> i <c>sub-delay</c>), pa se korekcija primjenjuje odmah,
/// bez prepisivanja datoteke. Pomak rješava titlove koji stalno kasne ili žure, a brzina rješava
/// titlove koji kroz film sve više kasne (npr. titlovi za 25 fps na videu od 23,976 fps).
/// </para>
/// </summary>
public readonly record struct SubtitleTiming(double Speed, double DelayMs)
{
    public static SubtitleTiming Identity => new(1.0, 0);

    /// <summary>Česte razlike u broju sličica: titlovi rađeni za jedan fps, video u drugom.</summary>
    public static IReadOnlyList<(string Name, double Speed)> FrameRatePresets { get; } =
    [
        ("Titlovi 25 fps → video 23,976 fps", 25.0 / (24000.0 / 1001.0)),
        ("Titlovi 23,976 fps → video 25 fps", 24000.0 / 1001.0 / 25.0),
        ("Titlovi 25 fps → video 24 fps", 25.0 / 24.0),
        ("Titlovi 24 fps → video 25 fps", 24.0 / 25.0),
        ("Titlovi 24 fps → video 23,976 fps", 24.0 / (24000.0 / 1001.0)),
        ("Titlovi 23,976 fps → video 24 fps", 24000.0 / 1001.0 / 24.0),
    ];

    public bool IsIdentity => Math.Abs(Speed - 1) < 1e-9 && Math.Abs(DelayMs) < 0.5;

    public double Apply(double originalMs) => originalMs * Speed + DelayMs;

    public double Invert(double displayMs) => (displayMs - DelayMs) / Speed;

    /// <summary>Jedna točka: zadržava brzinu i računa pomak tako da se original poklopi s ciljem.</summary>
    public SubtitleTiming AlignPoint(double originalMs, double targetMs) => new(Speed, targetMs - originalMs * Speed);

    /// <summary>
    /// Nova brzina uz sidro: replika koja je u trenutku <paramref name="anchorDisplayMs"/> na ekranu ostaje točno gdje jest,
    /// a mijenja se samo ono dalje (ili ranije) u filmu. Za "titlovi postupno kasne/žure" od mjesta gdje su točni.
    /// </summary>
    public SubtitleTiming WithSpeedAnchored(double speed, double anchorDisplayMs)
    {
        double original = Invert(anchorDisplayMs);
        return new SubtitleTiming(speed, anchorDisplayMs - original * speed);
    }

    /// <summary>Koliko se titlovi pomaknu kroz 10 minuta filma zbog brzine (s); 0 = bez postupne korekcije.</summary>
    public double DriftSecondsPerTenMinutes => (Speed - 1) * 600;

    /// <summary>Dvije točke (npr. replika na početku i na kraju filma): računa i brzinu i pomak.</summary>
    public static SubtitleTiming FromTwoPoints(double original1, double target1, double original2, double target2)
    {
        if (Math.Abs(original2 - original1) < 1)
        {
            throw new ArgumentException("Točke moraju biti na različitim mjestima u filmu.");
        }

        double speed = (target2 - target1) / (original2 - original1);
        return new SubtitleTiming(speed, target1 - speed * original1);
    }

    public SubtitleDocument ApplyTo(SubtitleDocument doc)
    {
        var result = new SubtitleDocument(doc.Format)
        {
            AssHeader = doc.AssHeader,
            AssEventFormat = doc.AssEventFormat,
            AssTrailer = doc.AssTrailer,
            FrameRate = doc.FrameRate,
        };

        foreach (SubtitleCue cue in doc.Cues)
        {
            SubtitleCue copy = cue.Clone();
            copy.StartMs = (long)Math.Round(Apply(cue.StartMs));
            copy.EndMs = (long)Math.Round(Apply(cue.EndMs));
            result.Cues.Add(copy);
        }

        return result;
    }
}
