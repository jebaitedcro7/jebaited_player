using System.Globalization;

namespace JebaitedPlayer.Subtitles;

/// <summary>
/// Korekcija vremena po dijelovima: titlovi za drugo izdanje (TV, DVD, BluRay) imaju pomak koji se mijenja na prijelazima
/// scena. Svaki dio ima svoj pomak (i brzinu); replika se preslikava prema dijelu u kojem počinje, a trajanje zadržava
/// (pomnoženo brzinom dijela).
/// <para>mpv zna samo jedan <c>sub-delay</c>, pa player od ove karte napravi ispravljenu kopiju titla (original se ne mijenja).</para>
/// </summary>
public sealed class SubtitleTimeMap
{
    /// <param name="FromMs">Početak dijela u izvornom vremenu titla (ms); prvi dio vrijedi i za sve prije njega.</param>
    public readonly record struct Segment(double FromMs, double Speed, double OffsetMs)
    {
        public double Apply(double ms) => ms * Speed + OffsetMs;
    }

    public SubtitleTimeMap(IEnumerable<Segment> segments)
    {
        Segments = [.. segments.OrderBy(s => s.FromMs)];
        if (Segments.Count == 0)
        {
            throw new ArgumentException("Karta vremena mora imati barem jedan dio.", nameof(segments));
        }
    }

    public IReadOnlyList<Segment> Segments { get; }

    public double MinOffsetMs => Segments.Min(s => s.OffsetMs);

    public double MaxOffsetMs => Segments.Max(s => s.OffsetMs);

    public Segment For(double originalMs)
    {
        Segment current = Segments[0];
        foreach (Segment s in Segments)
        {
            if (s.FromMs <= originalMs)
            {
                current = s;
            }
            else
            {
                break;
            }
        }

        return current;
    }

    public double Apply(double originalMs) => For(originalMs).Apply(originalMs);

    /// <summary>Nova kopija dokumenta s preslikanim vremenima (replika ostaje u dijelu u kojem počinje).</summary>
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
            Segment s = For(cue.StartMs);
            SubtitleCue copy = cue.Clone();
            copy.StartMs = Math.Max(0, (long)Math.Round(s.Apply(cue.StartMs)));
            copy.EndMs = Math.Max(copy.StartMs + 1, (long)Math.Round(s.Apply(cue.EndMs)));
            result.Cues.Add(copy);
        }

        return result;
    }

    /// <summary>Kratak zapis za bazu: "od:brzina:pomak;…" (ms, invarijantni brojevi).</summary>
    public string Serialize() => string.Join(";", Segments.Select(s =>
        string.Create(CultureInfo.InvariantCulture, $"{s.FromMs:R}:{s.Speed:R}:{s.OffsetMs:R}")));

    public static SubtitleTimeMap? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var segments = new List<Segment>();
        foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] f = part.Split(':');
            if (f.Length != 3
                || !double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double from)
                || !double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double speed)
                || !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double offset)
                || speed is <= 0.5 or >= 2 || !double.IsFinite(from) || !double.IsFinite(offset))
            {
                return null;
            }

            segments.Add(new Segment(from, speed, offset));
        }

        return segments.Count == 0 ? null : new SubtitleTimeMap(segments);
    }

    /// <summary>Stabilan kratki ključ (za naziv ispravljene kopije u predmemoriji).</summary>
    public string Hash() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Serialize())))[..12]
        .ToLowerInvariant();
}
