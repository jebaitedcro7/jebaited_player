namespace JebaitedPlayer.Library;

/// <summary>
/// Koliko je videa stvarno odgledano (ciklus 1, M6b). Video je podijeljen na 100 jednakih dijelova i za svaki se
/// broji odgledano vrijeme. Premotavanje se ne broji, a ponovno gledanje istog dijela ne povećava pokrivenost,
/// pa brzo „prolaženje” kroz epizodu ne označi epizodu pogledanom.
/// </summary>
public sealed class WatchCoverage
{
    public const int Buckets = 100;

    /// <summary>Video je pogledan kad je stvarno odgledano barem ovoliko (odjavna špica se smije preskočiti).</summary>
    public const double WatchedFraction = 0.8;

    private readonly double[] _filled = new double[Buckets];

    /// <summary>Odgledani udio videa, 0–1.</summary>
    public double Fraction => Math.Clamp(_filled.Sum() / Buckets, 0, 1);

    public bool IsWatched => Fraction >= WatchedFraction - 1e-9;

    /// <summary>Dodaj odgledani dio videa [<paramref name="from"/>, <paramref name="to"/>] (sekunde).</summary>
    public void AddPlayed(double from, double to, double duration)
    {
        if (!(duration > 0) || !double.IsFinite(duration) || !double.IsFinite(from) || !double.IsFinite(to))
        {
            return;
        }

        from = Math.Clamp(from, 0, duration);
        to = Math.Clamp(to, 0, duration);
        if (to <= from)
        {
            return;
        }

        double size = duration / Buckets;
        int first = Math.Min(Buckets - 1, (int)(from / size));
        int last = Math.Min(Buckets - 1, (int)(to / size));
        for (int b = first; b <= last; b++)
        {
            double start = b * size;
            double overlap = Math.Min(to, start + size) - Math.Max(from, start);
            if (overlap > 0)
            {
                _filled[b] = Math.Min(1, _filled[b] + overlap / size);
            }
        }
    }

    public void Clear() => Array.Clear(_filled);

    /// <summary>Zapis za bazu: 100 bajtova (0–255 po dijelu) u Base64.</summary>
    public string ToText()
    {
        var bytes = new byte[Buckets];
        for (int i = 0; i < Buckets; i++)
        {
            bytes[i] = (byte)Math.Round(Math.Clamp(_filled[i], 0, 1) * 255);
        }

        return Convert.ToBase64String(bytes);
    }

    /// <summary>Zapis iz baze; null ako ga nema ili je oštećen.</summary>
    public static WatchCoverage? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(text);
            if (bytes.Length != Buckets)
            {
                return null;
            }

            var coverage = new WatchCoverage();
            for (int i = 0; i < Buckets; i++)
            {
                coverage._filled[i] = bytes[i] / 255.0;
            }

            return coverage;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Pokrivenost za zapis iz baze. Zapisi iz verzija prije 1.3.0 je nemaju: tada se pretpostavlja da je video
    /// gledan od početka do spremljene pozicije (inače se film započet u 1.2.0 nikad ne bi mogao označiti pogledanim).
    /// </summary>
    public static WatchCoverage FromEntry(WatchEntry? entry)
    {
        if (Parse(entry?.Coverage) is { } stored)
        {
            return stored;
        }

        var seeded = new WatchCoverage();
        if (entry is { Watched: false })
        {
            seeded.AddPlayed(0, entry.Position, entry.Duration);
        }

        return seeded;
    }
}
