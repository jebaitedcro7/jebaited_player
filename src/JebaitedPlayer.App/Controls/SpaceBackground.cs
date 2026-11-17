using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JebaitedPlayer.Views;

namespace JebaitedPlayer.Controls;

/// <summary>
/// Diskretna svemirska pozadina početnog ekrana: tamni prijelaz, blagi sjajevi (zeleni kao zraka na logu i plavi),
/// sitne zvijezde i vinjeta. Svijetla tema dobiva istu kompoziciju u svijetlim tonovima.
/// <para>
/// Bez animacija i bez slikovnih datoteka: crta se vektorski samo kad se promijeni veličina ili tema (WPF zadrži
/// nacrtano), pa u mirovanju ne troši CPU, a zauzima tek nekoliko KB. Zvijezde su uvijek na istom mjestu
/// (fiksno sjeme), u relativnim koordinatama, pa se ne "miču" kad se prozor poveća.
/// </para>
/// </summary>
public sealed class SpaceBackground : FrameworkElement
{
    private static readonly Star[] Stars = CreateStars();

    // Fini šum preko prijelaza: sučelje se iscrtava bez GPU-a i bez ditheringa, pa bi se u blagim prijelazima
    // vidjeli prstenovi. Mala pločica 128×128 (64 KB) ponavlja se preko cijele površine.
    private static readonly ImageBrush Grain = CreateGrain();

    public SpaceBackground()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            WindowTheme.Changed -= InvalidateVisual;
            WindowTheme.Changed += InvalidateVisual;
        };
        Unloaded += (_, _) => WindowTheme.Changed -= InvalidateVisual;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 1 || h < 1)
        {
            return;
        }

        bool dark = WindowTheme.IsDark;
        var rect = new Rect(0, 0, w, h);

        // Osnova: tamnoplavo prema crnom (svijetla: blijedo plavkasto nebo).
        var baseBrush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.38),
            GradientOrigin = new Point(0.5, 0.38),
            RadiusX = 0.85,
            RadiusY = 0.95,
            GradientStops =
            {
                new GradientStop(dark ? Color.FromRgb(0x10, 0x18, 0x2A) : Color.FromRgb(0xF6, 0xF8, 0xFC), 0),
                new GradientStop(dark ? Color.FromRgb(0x06, 0x08, 0x0F) : Color.FromRgb(0xE4, 0xEA, 0xF3), 1),
            },
        };
        baseBrush.Freeze();
        dc.DrawRectangle(baseBrush, null, rect);

        // Dva vrlo blaga sjaja (maglice): zeleni gore lijevo, plavi dolje desno.
        DrawGlow(dc, w, h, 0.22, 0.24, 0.42, dark ? Color.FromArgb(0x26, 0x4C, 0x9A, 0x55) : Color.FromArgb(0x1C, 0x6F, 0xB8, 0x7C));
        DrawGlow(dc, w, h, 0.82, 0.80, 0.48, dark ? Color.FromArgb(0x24, 0x3A, 0x55, 0xA8) : Color.FromArgb(0x18, 0x7A, 0x96, 0xD4));
        dc.DrawRectangle(Grain, null, rect);

        // Zvijezde: na svijetloj temi manje i sive.
        double scale = Math.Clamp(Math.Sqrt(w * h) / 1100, 0.7, 1.6);
        foreach (Star s in Stars)
        {
            if (!dark && s.Index % 2 == 1)
            {
                continue;
            }

            var center = new Point(s.X * w, s.Y * h);
            double radius = s.Size * scale;
            byte alpha = (byte)Math.Round((dark ? s.Brightness : s.Brightness * 0.45) * 255);
            Color color = dark ? Color.FromArgb(alpha, s.Tint.R, s.Tint.G, s.Tint.B) : Color.FromArgb(alpha, 0x6E, 0x7E, 0x98);
            if (s.Halo && dark)
            {
                DrawHalo(dc, center, radius * 4.5, Color.FromArgb((byte)(alpha / 4), s.Tint.R, s.Tint.G, s.Tint.B));
            }

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            dc.DrawEllipse(brush, null, center, radius, radius);
        }

        // Vinjeta: rubovi tamniji, pa sadržaj u sredini dobije dubinu.
        if (dark)
        {
            var vignette = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.45),
                GradientOrigin = new Point(0.5, 0.45),
                RadiusX = 0.75,
                RadiusY = 0.8,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.55),
                    new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 1),
                },
            };
            vignette.Freeze();
            dc.DrawRectangle(vignette, null, rect);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    private static void DrawGlow(DrawingContext dc, double w, double h, double x, double y, double radius, Color color)
    {
        var brush = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B));
        brush.Freeze();
        double r = radius * Math.Max(w, h);
        dc.DrawEllipse(brush, null, new Point(x * w, y * h), r, r * 0.7);
    }

    private static void DrawHalo(DrawingContext dc, Point center, double radius, Color color)
    {
        var brush = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B));
        brush.Freeze();
        dc.DrawEllipse(brush, null, center, radius, radius);
    }

    private static ImageBrush CreateGrain()
    {
        const int size = 128;
        var pixels = new byte[size * size * 4];
        var random = new Random(117);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte v = (byte)random.Next(256);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
            pixels[i + 3] = 9;
        }

        BitmapSource bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Uvijek iste zvijezde (fiksno sjeme): većina sitnih i slabih, nekoliko jačih sa sjajem.</summary>
    private static Star[] CreateStars()
    {
        var random = new Random(20261117);
        var tints = new[] { Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xDC, 0xE6, 0xFF), Color.FromRgb(0xFF, 0xF1, 0xD6), Color.FromRgb(0xD8, 0xF5, 0xE0) };
        var stars = new Star[240];
        for (int i = 0; i < stars.Length; i++)
        {
            double t = random.NextDouble();
            bool bright = i % 24 == 0;
            stars[i] = new Star(
                i,
                random.NextDouble(),
                random.NextDouble(),
                bright ? 1.25 + random.NextDouble() * 0.5 : 0.45 + t * t * 0.75,
                bright ? 0.85 : 0.18 + random.NextDouble() * 0.55,
                tints[random.Next(tints.Length)],
                bright);
        }

        return stars;
    }

    private readonly record struct Star(int Index, double X, double Y, double Size, double Brightness, Color Tint, bool Halo);
}
