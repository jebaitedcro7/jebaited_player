using System.Windows;
using System.Windows.Media;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.Views;

/// <summary>
/// 2.0: birač vlastite boje isticanja bez dodatne biblioteke: nijansa, zasićenost i svjetlina (HSL) ili kod #RRGGBB.
/// Svaka promjena javlja se odmah (Previewing), pa se boja vidi u cijelom playeru; Odustani vraća prijašnju.
/// </summary>
public partial class ColorPickerWindow : Window
{
    private bool _updating;

    internal ColorPickerWindow(string startHex)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        if (!AccentColors.TryParse(startHex, out Color start))
        {
            AccentColors.TryParse(AccentColors.DefaultHex, out start);
        }

        SetColor(start, updateHex: true);
        HueSlider.ValueChanged += (_, _) => FromSliders();
        SaturationSlider.ValueChanged += (_, _) => FromSliders();
        LightnessSlider.ValueChanged += (_, _) => FromSliders();
        HexBox.TextChanged += (_, _) => FromHex();
    }

    /// <summary>Odabrana boja (#RRGGBB) kakvu je korisnik zadao (prilagodba za temu radi se pri primjeni).</summary>
    public string Hex { get; private set; } = AccentColors.DefaultHex;

    /// <summary>Svaka promjena boje (za pregled uživo u cijelom playeru).</summary>
    internal event Action<string>? Previewing;

    private void SetColor(Color color, bool updateHex)
    {
        _updating = true;
        (double h, double s, double l) = AccentColors.ToHsl(color);
        HueSlider.Value = h;
        SaturationSlider.Value = s * 100;
        LightnessSlider.Value = Math.Clamp(l * 100, LightnessSlider.Minimum, LightnessSlider.Maximum);
        if (updateHex)
        {
            HexBox.Text = AccentColors.ToHex(color);
        }

        _updating = false;
        Show(color);
    }

    private void FromSliders()
    {
        if (_updating)
        {
            return;
        }

        Color color = AccentColors.FromHsl(HueSlider.Value, SaturationSlider.Value / 100, LightnessSlider.Value / 100);
        _updating = true;
        HexBox.Text = AccentColors.ToHex(color);
        _updating = false;
        Show(color);
    }

    private void FromHex()
    {
        if (!_updating && AccentColors.TryParse(HexBox.Text, out Color color))
        {
            SetColor(color, updateHex: false);
        }
    }

    private void Show(Color color)
    {
        Hex = AccentColors.ToHex(color);
        AccentPalette palette = AccentColors.For(Hex, WindowTheme.IsDark);
        PreviewBox.Background = WindowTheme.Frozen(palette.Accent);
        PreviewText.Foreground = WindowTheme.Frozen(palette.AccentForeground);
        Previewing?.Invoke(Hex);
    }

    /// <summary>Dijagnostika: upis koda boje kao u polje.</summary>
    internal void SetHexForTests(string hex) => HexBox.Text = hex;

    /// <summary>Dijagnostika: pomak klizača nijanse.</summary>
    internal void SetHueForTests(double hue) => HueSlider.Value = hue;

    internal Color PreviewColor => ((SolidColorBrush)PreviewBox.Background).Color;

    private void OnUse(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
