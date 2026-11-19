using System.Globalization;
using System.Text.Json.Serialization;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Equalizer (ciklus 1, V2): 10 pojaseva, pojačanje (preamp), ugrađeni preseti i jedan vlastiti preset.
/// Sprema se u settings.json; stare datoteke ga nemaju, pa vrijedi zadano (isključen, Flat).
/// </summary>
public sealed class EqualizerSettings
{
    /// <summary>Equalizer uključen. Zadano isključen: zvuk ostaje točno kao u datoteci.</summary>
    public bool Enabled { get; set; }

    /// <summary>Odabrani preset (id iz <see cref="Equalizer.Presets"/>) ili "custom".</summary>
    public string Preset { get; set; } = "flat";

    /// <summary>Pojačanje prije pojaseva, u dB.</summary>
    public double Preamp { get; set; }

    /// <summary>Vlastiti preset: pojačanje 10 pojaseva u dB (vrijedi kad je odabran "custom").</summary>
    public double[] CustomGains { get; set; } = new double[Equalizer.BandCount];

    /// <summary>Pojačanja koja se stvarno primjenjuju (odabrani preset ili vlastiti).</summary>
    [JsonIgnore]
    public double[] Gains => Preset == Equalizer.Custom
        ? [.. CustomGains]
        : [.. Equalizer.PresetGains(Preset)];

    /// <summary>Ručno uređena ili oštećena datoteka: nepoznat preset, kriva duljina i vrijednosti izvan raspona.</summary>
    public void Normalize()
    {
        if (Preset != Equalizer.Custom && !Equalizer.Presets.Any(p => p.Id == Preset))
        {
            Preset = "flat";
        }

        double[] gains = new double[Equalizer.BandCount];
        for (int i = 0; i < gains.Length && i < (CustomGains?.Length ?? 0); i++)
        {
            gains[i] = Equalizer.Clamp(CustomGains![i]);
        }

        CustomGains = gains;
        Preamp = Equalizer.Clamp(Preamp);
    }
}

/// <summary>Mini Music Player (ciklus 2, V1): položaj, veličina i izgled se pamte.</summary>
public sealed class MiniPlayerSettings
{
    public const double MinScale = 0.75;
    public const double MaxScale = 2.5;

    public double? Left { get; set; }
    public double? Top { get; set; }

    /// <summary>Povećanje (1 = 100 %), mijenja se povlačenjem kuta, Ctrl + kotačić ili Ctrl + / −.</summary>
    public double Scale { get; set; } = 1;

    /// <summary>Uska traka (dizajn 3) umjesto punog izgleda (dizajn 1).</summary>
    public bool Compact { get; set; }

    public bool ShowEqualizer { get; set; }

    public bool ShowPlaylist { get; set; }

    public void Normalize() =>
        Scale = double.IsFinite(Scale) ? Math.Clamp(Math.Round(Scale, 2), MinScale, MaxScale) : 1;
}

/// <summary>Pojasevi, preseti i mpv/ffmpeg filtar za equalizer.</summary>
public static class Equalizer
{
    public const int BandCount = 10;

    /// <summary>Najveće pojačanje ili prigušenje jednog pojasa (dB).</summary>
    public const double MaxGain = 12;

    public const string Custom = "custom";

    /// <summary>Središnje frekvencije pojaseva (Hz), jedna oktava po pojasu.</summary>
    public static readonly IReadOnlyList<int> Frequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>Ugrađeni preseti (id = ključ prijevoda "eq.&lt;id&gt;"), dB po pojasu od 31 Hz do 16 kHz.</summary>
    public static readonly IReadOnlyList<(string Id, double[] Gains)> Presets =
    [
        ("flat", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        ("rock", [5, 4, 3, 1, -1, -1, 1, 3, 4, 5]),
        ("pop", [-1, 1, 3, 4, 3, 0, -1, -1, 1, 2]),
        ("jazz", [3, 2, 1, 2, -1, -1, 0, 1, 2, 3]),
        ("classical", [4, 3, 2, 1, 0, 0, 0, 1, 3, 4]),
        ("electronic", [5, 4, 1, 0, -2, 1, 0, 1, 4, 5]),
        ("bass", [7, 6, 4, 2, 0, 0, 0, 0, 0, 0]),
        ("treble", [0, 0, 0, 0, 0, 1, 2, 4, 5, 6]),
        ("vocal", [-2, -2, -1, 1, 3, 4, 3, 1, 0, -1]),
    ];

    public static IReadOnlyList<double> PresetGains(string id) =>
        Presets.FirstOrDefault(p => p.Id == id).Gains ?? Presets[0].Gains;

    public static double Clamp(double gain) =>
        double.IsFinite(gain) ? Math.Clamp(Math.Round(gain * 2) / 2, -MaxGain, MaxGain) : 0;

    /// <summary>Oznaka pojasa ispod klizača ("31", "1k", "16k").</summary>
    public static string FrequencyLabel(int hz) => hz >= 1000 ? $"{hz / 1000}k" : hz.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// ffmpeg filtri za lavfi graf (bez "lavfi=[...]"), ili null kad equalizer ništa ne mijenja (isključen ili sve 0 dB).
    /// Pojasevi s 0 dB se preskaču (manje CPU-a). Kad se pojasevi pojačavaju, ukupna razina se spusti za najveće
    /// pojačanje, da zvuk ne puca (clipping); preamp je ručna korekcija iznad toga.
    /// </summary>
    public static string? BuildFilters(EqualizerSettings? s)
    {
        if (s is not { Enabled: true })
        {
            return null;
        }

        double[] gains = s.Gains;
        double preamp = Clamp(s.Preamp);
        var parts = new List<string>();
        double headroom = Math.Max(0, gains.Max());
        double level = preamp - headroom;
        if (Math.Abs(level) >= 0.05)
        {
            parts.Add($"volume={F(level)}dB");
        }

        for (int i = 0; i < BandCount && i < gains.Length; i++)
        {
            double g = Clamp(gains[i]);
            if (Math.Abs(g) >= 0.05)
            {
                parts.Add($"equalizer=f={Frequencies[i]}:t=o:w=1:g={F(g)}");
            }
        }

        return parts.Count == 0 ? null : string.Join(',', parts);
    }

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
