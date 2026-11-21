using System.Windows;
using System.Windows.Media;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.Controls;

/// <summary>
/// 2.1.9: traka napretka preuzimanja nadogradnje. Zaobljena traka u boji isticanja koja se glatko puni, uz svjetlucavu
/// prugu koja neprekidno klizi preko ispunjenog dijela i postotak u sredini. Prije prvog podatka (0 %) klizi tamniji
/// val. Animacija radi samo dok je traka vidljiva (CompositionTarget.Rendering, ~30 sličica u sekundi).
/// </summary>
public sealed class DownloadProgressBar : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(DownloadProgressBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((DownloadProgressBar)d)._target = ((DownloadProgressBar)d).Percent));

    private double _shown;
    private double _target;
    private double _phase;
    private TimeSpan _last;
    private bool _running;

    public DownloadProgressBar()
    {
        Height = 26;
        MinWidth = 200;
        IsVisibleChanged += (_, _) => UpdateRunning();
        Loaded += (_, _) => UpdateRunning();
        Unloaded += (_, _) => Stop();
    }

    /// <summary>Postotak 0-100.</summary>
    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, Math.Clamp(value, 0, 100));
    }

    /// <summary>Animacija trenutno radi (traka je vidljiva). Za provjeru.</summary>
    public bool IsAnimating => _running;

    private void UpdateRunning()
    {
        if (IsVisible && IsLoaded)
        {
            if (!_running)
            {
                _running = true;
                _last = TimeSpan.Zero;
                CompositionTarget.Rendering += OnRendering;
            }
        }
        else
        {
            Stop();
        }
    }

    private void Stop()
    {
        if (_running)
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        TimeSpan now = ((RenderingEventArgs)e).RenderingTime;
        if (_last != TimeSpan.Zero && (now - _last).TotalMilliseconds < 33)
        {
            return;
        }

        double dt = _last == TimeSpan.Zero ? 0.033 : (now - _last).TotalSeconds;
        _last = now;
        _phase = (_phase + dt * 0.55) % 1.0;
        // Ispuna glatko prati stvarni postotak (ne skače pri svakom izvješću).
        _shown += (_target - _shown) * Math.Min(1, dt * 8);
        if (Math.Abs(_target - _shown) < 0.05)
        {
            _shown = _target;
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 4)
        {
            return;
        }

        double radius = h / 2;
        Color accent = (TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x4F, 0xD1, 0xC5);
        Brush text = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;

        // Staza.
        var track = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80));
        dc.DrawRoundedRectangle(track, new Pen(new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B)), 1), new Rect(0.5, 0.5, w - 1, h - 1), radius, radius);

        double fillWidth = Math.Max(h, (w - 2) * _shown / 100.0);
        if (_shown <= 0.01)
        {
            // Bez podataka: val koji klizi po stazi.
            double band = w * 0.28;
            double x = -band + (w + band) * _phase;
            dc.PushClip(new RectangleGeometry(new Rect(1, 1, w - 2, h - 2), radius - 1, radius - 1));
            var wave = new LinearGradientBrush(
                [new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 0),
                 new GradientStop(Color.FromArgb(0x90, accent.R, accent.G, accent.B), 0.5),
                 new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1)], new Point(0, 0), new Point(1, 0));
            dc.DrawRectangle(wave, null, new Rect(x, 1, band, h - 2));
            dc.Pop();
        }
        else
        {
            var fillRect = new Rect(1, 1, Math.Min(fillWidth, w - 2), h - 2);
            dc.PushClip(new RectangleGeometry(fillRect, radius - 1, radius - 1));
            var fill = new LinearGradientBrush(
                [new GradientStop(Lighten(accent, -0.18), 0), new GradientStop(accent, 0.55), new GradientStop(Lighten(accent, 0.22), 1)],
                new Point(0, 0), new Point(1, 0));
            dc.DrawRectangle(fill, null, fillRect);

            // Svjetlucava pruga koja stalno klizi preko ispunjenog dijela.
            double band = Math.Max(40, h * 3);
            double x = fillRect.Left - band + (fillRect.Width + band) * _phase;
            var shine = new LinearGradientBrush(
                [new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                 new GradientStop(Color.FromArgb(0x70, 255, 255, 255), 0.5),
                 new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)], new Point(0, 0), new Point(1, 0));
            dc.DrawRectangle(shine, null, new Rect(x, fillRect.Top, band, fillRect.Height));
            // Gornji sjaj (staklasti dojam).
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), null, new Rect(fillRect.Left, fillRect.Top, fillRect.Width, fillRect.Height * 0.42));
            dc.Pop();
        }

        var label = new FormattedText($"{Math.Round(_shown):0} %", Loc.Culture ?? System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Semibold"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            Math.Max(10, h * 0.5), text, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label, new Point((w - label.Width) / 2, (h - label.Height) / 2));
    }

    private static Color Lighten(Color c, double amount)
    {
        static byte Mix(byte v, double a) => (byte)Math.Clamp(a >= 0 ? v + (255 - v) * a : v * (1 + a), 0, 255);
        return Color.FromRgb(Mix(c.R, amount), Mix(c.G, amount), Mix(c.B, amount));
    }
}
