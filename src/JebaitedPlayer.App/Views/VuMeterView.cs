using System.Windows;
using System.Windows.Media;

namespace JebaitedPlayer.Views;

/// <summary>
/// VU metar mini playera (1.4.1): lijevi i desni kanal, -60..0 dB. Izgledi: "bars" (trake), "led" (segmenti
/// zeleno-žuto-crveno), "lines" (tanke linije s oznakom vrha) i "off". Crta se samo kad stigne nova razina (10× u sekundi).
/// </summary>
public sealed class VuMeterView : FrameworkElement
{
    private const double Floor = -60;
    private const int Segments = 16;

    private static readonly Brush Green = Frozen(0x7B, 0xD8, 0x8F);
    private static readonly Brush Yellow = Frozen(0xE8, 0xD4, 0x4D);
    private static readonly Brush Red = Frozen(0xE8, 0x57, 0x4D);
    private static readonly Brush Track = Frozen(0x17, 0x29, 0x1C);
    private static readonly Brush Dim = Frozen(0x22, 0x3A, 0x28);

    private readonly double[] _level = [Floor, Floor];
    private readonly double[] _peak = [Floor, Floor];
    private readonly long[] _peakAt = [0, 0];
    private string _style = "bars";

    public string VuStyle
    {
        get => _style;
        set
        {
            _style = value;
            InvalidateVisual();
        }
    }

    public void SetLevels(double left, double right)
    {
        long now = Environment.TickCount64;
        double[] values = [left, right];
        for (int i = 0; i < 2; i++)
        {
            _level[i] = values[i];
            if (values[i] >= _peak[i] || now - _peakAt[i] > 1200)
            {
                _peak[i] = Math.Max(values[i], _peak[i] - 3);
                _peakAt[i] = values[i] >= _peak[i] ? now : _peakAt[i];
            }
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (_style == "off" || w <= 0 || h <= 0)
        {
            return;
        }

        double row = (h - 2) / 2;
        for (int ch = 0; ch < 2; ch++)
        {
            double y = ch * (row + 2);
            double f = Fraction(_level[ch]);
            switch (_style)
            {
                case "led":
                {
                    double seg = (w - (Segments - 1)) / Segments;
                    for (int i = 0; i < Segments; i++)
                    {
                        bool lit = i < Math.Round(f * Segments);
                        Brush color = i >= Segments - 2 ? Red : i >= Segments - 5 ? Yellow : Green;
                        dc.DrawRectangle(lit ? color : Dim, null, new Rect(i * (seg + 1), y, seg, row));
                    }

                    break;
                }

                case "lines":
                {
                    double line = Math.Max(1, row / 3);
                    double ly = y + (row - line) / 2;
                    dc.DrawRectangle(Dim, null, new Rect(0, ly, w, line));
                    dc.DrawRectangle(Green, null, new Rect(0, ly, f * w, line));
                    double p = Fraction(_peak[ch]) * w;
                    dc.DrawRectangle(_peak[ch] > -6 ? Red : Yellow, null, new Rect(Math.Max(0, p - 2), y, 2, row));
                    break;
                }

                default:
                    dc.DrawRectangle(Track, null, new Rect(0, y, w, row));
                    dc.DrawRectangle(Green, null, new Rect(0, y, f * w, row));
                    break;
            }
        }
    }

    private static double Fraction(double db) => (Math.Clamp(double.IsFinite(db) ? db : Floor, Floor, 0) - Floor) / -Floor;

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
