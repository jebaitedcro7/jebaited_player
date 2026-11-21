using System.Globalization;
using System.Windows.Media;

namespace JebaitedPlayer.Infrastructure;

/// <summary>Boje izvedene iz boje isticanja za jednu temu (i za mini LCD, koji je uvijek taman).</summary>
public sealed record AccentPalette(
    Color Accent, Color AccentForeground, Color Selection,
    Color LcdText, Color LcdBorder, Color LcdTrack, Color LcdBack, Color LcdTag, Color LcdDim);

/// <summary>
/// 2.0: boja isticanja (gumbi, trake, označeno, mini LCD). Gotove boje ili vlastita #RRGGBB. Iz odabrane boje računaju se
/// izvedene boje, a u svakoj temi boja se po potrebi posvijetli ili potamni da kontrast s pozadinom bude najmanje 3:1
/// (WCAG za grafičke elemente) i da tekst na gumbu ostane čitljiv. Pozadina ostaje tamna ili svijetla tema.
/// </summary>
public static class AccentColors
{
    /// <summary>Zadana zelena (ista kao u Themes/Dark.xaml); kao zadana se ne sprema.</summary>
    public const string DefaultHex = "#7BD88F";

    public static readonly IReadOnlyList<(string Id, string Hex)> Presets =
    [
        ("green", DefaultHex),
        ("blue", "#6CB4FF"),
        ("teal", "#4FD1C5"),
        ("purple", "#B794F6"),
        ("pink", "#F687B3"),
        ("red", "#FF7A7A"),
        ("orange", "#FFA45C"),
        ("yellow", "#F6D55C"),
    ];

    /// <summary>Naziv gotove boje (oblačić kružića).</summary>
    public static string PresetName(string id) => id switch
    {
        "green" => Loc.T("accent.green"),
        "blue" => Loc.T("accent.blue"),
        "teal" => Loc.T("accent.teal"),
        "purple" => Loc.T("accent.purple"),
        "pink" => Loc.T("accent.pink"),
        "red" => Loc.T("accent.red"),
        "orange" => Loc.T("accent.orange"),
        _ => Loc.T("accent.yellow"),
    };

    private static readonly Color DarkBackground = Color.FromRgb(0x12, 0x12, 0x12);
    private static readonly Color LightBackground = Color.FromRgb(0xF3, 0xF3, 0xF3);
    private static readonly Color LcdBackground = Color.FromRgb(0x0B, 0x10, 0x0C);

    /// <summary>Samo #RRGGBB (6 heksadekadskih znamenki).</summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        string t = (text ?? string.Empty).Trim();
        if (t.StartsWith('#'))
        {
            t = t[1..];
        }

        if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            return false;
        }

        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Spremljena vrijednost: "" za zadanu zelenu, inače #RRGGBB velikim slovima; neispravno → "".</summary>
    public static string Normalize(string? text) =>
        TryParse(text, out Color c) && !string.Equals(ToHex(c), DefaultHex, StringComparison.Ordinal) ? ToHex(c) : string.Empty;

    /// <summary>Relativna svjetlina (WCAG 2.x).</summary>
    public static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
        (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
        (byte)Math.Round(from.B + ((to.B - from.B) * amount)));

    /// <summary>Posvijetli (lighten) ili potamni boju u koracima svjetline dok kontrast s pozadinom ne dosegne min.</summary>
    public static Color EnsureContrast(Color color, Color background, double min, bool lighten)
    {
        (double h, double s, double l) = ToHsl(color);
        Color result = color;
        for (int i = 0; i < 100 && Contrast(result, background) < min; i++)
        {
            l = Math.Clamp(l + (lighten ? 0.01 : -0.01), 0, 1);
            result = FromHsl(h, s, l);
        }

        return result;
    }

    /// <summary>Paleta za temu. Zadana zelena daje točno boje iz Dark.xaml / Light.xaml i mini playera.</summary>
    public static AccentPalette For(string? hex, bool dark)
    {
        if (!TryParse(hex, out Color chosen) || ToHex(chosen) == DefaultHex)
        {
            return dark
                ? new AccentPalette(Rgb(0x7BD88F), Rgb(0x0B1A0F), Rgb(0x2E4A35), Rgb(0x7BD88F), Rgb(0x2A4A32), Rgb(0x17291C), Rgb(0x0B100C), Rgb(0x1F3324), Rgb(0x223A28))
                : new AccentPalette(Rgb(0x1F8A3B), Rgb(0xFFFFFF), Rgb(0xD3EEDA), Rgb(0x7BD88F), Rgb(0x2A4A32), Rgb(0x17291C), Rgb(0x0B100C), Rgb(0x1F3324), Rgb(0x223A28));
        }

        Color background = dark ? DarkBackground : LightBackground;
        Color accent = EnsureContrast(chosen, background, 3, lighten: dark);
        // Tekst na gumbu: tamna nijansa iste boje ako je dovoljno čitljiva (4,5:1), inače crna ili bijela, što je čitljivije.
        Color darkText = Mix(Color.FromRgb(0, 0, 0), accent, 0.1);
        Color best = Contrast(accent, Colors.Black) >= Contrast(accent, Colors.White) ? Colors.Black : Colors.White;
        Color foreground = best == Colors.Black && Contrast(accent, darkText) >= 4.5 ? darkText : best;
        Color selection = dark ? Mix(background, accent, 0.28) : Mix(Colors.White, accent, 0.22);

        // Mini LCD je uvijek taman: tekst svijetao, okvir, traka, pozadina i oznaka izvora su tamne nijanse iste boje.
        Color lcdText = EnsureContrast(chosen, LcdBackground, 4.5, lighten: true);
        Color lcdBack = Mix(Color.FromRgb(0, 0, 0), lcdText, 0.07);
        return new AccentPalette(accent, foreground, selection,
            lcdText, Mix(lcdBack, lcdText, 0.25), Mix(lcdBack, lcdText, 0.11), lcdBack, Mix(lcdBack, lcdText, 0.18), Mix(lcdBack, lcdText, 0.2));
    }

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;
        if (max - min > 1e-9)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
            h *= 60;
        }

        return (h, s, l);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        static double Hue(double p, double q, double t)
        {
            t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
            return t < 1.0 / 6 ? p + ((q - p) * 6 * t) : t < 0.5 ? q : t < 2.0 / 3 ? p + ((q - p) * ((2.0 / 3) - t) * 6) : p;
        }

        double r = l, g = l, b = l;
        if (s > 1e-9)
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
            double p = (2 * l) - q;
            double hh = (h % 360) / 360.0;
            r = Hue(p, q, hh + (1.0 / 3));
            g = Hue(p, q, hh);
            b = Hue(p, q, hh - (1.0 / 3));
        }

        return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }
}
