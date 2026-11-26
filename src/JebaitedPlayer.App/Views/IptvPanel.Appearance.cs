using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.Views;

/// <summary>IPTV: izgled sekcije (font, veličina teksta, boja isticanja i pozadina). Vrijedi samo za IPTV panel, ne za cijeli player.</summary>
public partial class IptvPanel
{
    internal static readonly string[] Fonts =
        ["Segoe UI", "Segoe UI Variable Text", "Arial", "Verdana", "Tahoma", "Trebuchet MS", "Calibri", "Georgia", "Consolas"];

    internal static readonly (string Key, double Scale)[] Sizes =
        [("iptv.sizeSmall", 0.9), ("iptv.sizeNormal", 1.0), ("iptv.sizeLarge", 1.15), ("iptv.sizeXLarge", 1.3)];

    internal static readonly (string Key, string Hex)[] Accents =
    [
        ("iptv.colorGreen", "#7BD88F"), ("iptv.colorBlue", "#5AA9FF"), ("iptv.colorPurple", "#B48CFF"), ("iptv.colorOrange", "#FFB05A"),
        ("iptv.colorRed", "#FF6B6B"), ("iptv.colorPink", "#FF8AC8"), ("iptv.colorTeal", "#4FD1C5"),
    ];

    internal static readonly (string Key, string Hex)[] Backgrounds =
    [
        ("iptv.bgBlack", "#000000"), ("iptv.bgGray", "#1E1E1E"), ("iptv.bgNavy", "#0F1B33"), ("iptv.bgForest", "#10241A"), ("iptv.bgLight", "#F3F3F3"),
    ];

    /// <summary>Primijeni spremljeni izgled (poziva se pri pokretanju i nakon svake promjene).</summary>
    internal void ApplyAppearance()
    {
        if (string.IsNullOrWhiteSpace(_settings.FontFamily))
        {
            SetResourceReference(FontFamilyProperty, "UiFont");
        }
        else
        {
            FontFamily = new FontFamily(_settings.FontFamily);
        }

        double scale = Math.Clamp(_settings.TextScale <= 0 ? 1.0 : _settings.TextScale, 0.8, 1.5);
        if (Content is FrameworkElement root)
        {
            root.LayoutTransform = Math.Abs(scale - 1.0) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);
        }

        SetBrush("AccentBrush", TryColor(_settings.AccentColor));
        if (Selected is { } sel && IsLoaded)
        {
            Dispatcher.BeginInvoke(() => RebuildEpgStrip(sel.Channel));
        }

        SetBrush("AccentForegroundBrush", TryColor(_settings.AccentColor) is { } ac ? Luminance(ac) > 0.5 ? Color.FromRgb(0x10, 0x10, 0x10) : Colors.White : null);

        Color? bg = TryColor(_settings.BackgroundColor);
        SetBrush("PanelBackgroundBrush", bg);
        if (bg is { } b)
        {
            bool light = Luminance(b) > 0.5;
            SetBrush("TextPrimaryBrush", light ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Color.FromRgb(0xF3, 0xF3, 0xF3));
            SetBrush("TextSecondaryBrush", light ? Color.FromRgb(0x5C, 0x5C, 0x5C) : Color.FromRgb(0xA3, 0xA3, 0xA3));
            SetBrush("MenuBackgroundBrush", Shift(b, light ? -0.07 : 0.08));
            SetBrush("MenuBorderBrush", Shift(b, light ? -0.16 : 0.18));
            SetBrush("ButtonHoverBrush", Shift(b, light ? -0.1 : 0.12));
            SetBrush("SelectionBrush", Shift(b, light ? -0.2 : 0.22));
        }
        else
        {
            foreach (string k in new[] { "TextPrimaryBrush", "TextSecondaryBrush", "MenuBackgroundBrush", "MenuBorderBrush", "ButtonHoverBrush", "SelectionBrush" })
            {
                SetBrush(k, null);
            }
        }
    }

    private void SetBrush(string key, Color? color)
    {
        if (color is { } c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            Resources[key] = brush;
        }
        else
        {
            Resources.Remove(key);
        }
    }

    private static Color? TryColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    private static Color Shift(Color c, double amount)
    {
        static byte Mix(byte v, double a) => (byte)Math.Clamp(a >= 0 ? v + (255 - v) * a : v * (1 + a), 0, 255);
        return Color.FromRgb(Mix(c.R, amount), Mix(c.G, amount), Mix(c.B, amount));
    }

    /// <summary>Podizbornik „Izgled” u izborniku panela.</summary>
    private MenuItem AppearanceMenu()
    {
        var root = new MenuItem { Header = Loc.T("iptv.appearance") };

        var font = new MenuItem { Header = Loc.T("iptv.font") };
        font.Items.Add(Item(Loc.T("iptv.default"), () => SetAppearance(s => s.FontFamily = null), check: true, isChecked: string.IsNullOrWhiteSpace(_settings.FontFamily)));
        foreach (string f in Fonts)
        {
            string name = f;
            var mi = Item(name, () => SetAppearance(s => s.FontFamily = name), check: true, isChecked: _settings.FontFamily == name);
            mi.FontFamily = new FontFamily(name);
            font.Items.Add(mi);
        }

        var size = new MenuItem { Header = Loc.T("iptv.textSize") };
        foreach ((string key, double scale) in Sizes)
        {
            double sc = scale;
            size.Items.Add(Item(Loc.T(key), () => SetAppearance(s => s.TextScale = sc), check: true, isChecked: Math.Abs(_settings.TextScale - sc) < 0.001));
        }

        var accent = new MenuItem { Header = Loc.T("iptv.accentColor") };
        accent.Items.Add(Item(Loc.T("iptv.default"), () => SetAppearance(s => s.AccentColor = null), check: true, isChecked: _settings.AccentColor is null));
        foreach ((string key, string hex) in Accents)
        {
            string h = hex;
            accent.Items.Add(Swatch(Loc.T(key), h, () => SetAppearance(s => s.AccentColor = h), string.Equals(_settings.AccentColor, h, StringComparison.OrdinalIgnoreCase)));
        }

        var bgMenu = new MenuItem { Header = Loc.T("iptv.backgroundColor") };
        bgMenu.Items.Add(Item(Loc.T("iptv.default"), () => SetAppearance(s => s.BackgroundColor = null), check: true, isChecked: _settings.BackgroundColor is null));
        foreach ((string key, string hex) in Backgrounds)
        {
            string h = hex;
            bgMenu.Items.Add(Swatch(Loc.T(key), h, () => SetAppearance(s => s.BackgroundColor = h), string.Equals(_settings.BackgroundColor, h, StringComparison.OrdinalIgnoreCase)));
        }

        root.Items.Add(font);
        root.Items.Add(size);
        root.Items.Add(accent);
        root.Items.Add(bgMenu);
        root.Items.Add(new Separator());
        root.Items.Add(Item(Loc.T("iptv.resetAppearance"), () => SetAppearance(s =>
        {
            s.FontFamily = null;
            s.TextScale = 1.0;
            s.AccentColor = null;
            s.BackgroundColor = null;
        })));
        return root;
    }

    private static MenuItem Swatch(string header, string hex, Action action, bool isChecked)
    {
        var mi = Item(header, action, check: true, isChecked: isChecked);
        mi.Icon = new System.Windows.Shapes.Rectangle
        {
            Width = 14, Height = 14, RadiusX = 3, RadiusY = 3,
            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            Stroke = Brushes.Gray, StrokeThickness = 0.5,
        };
        return mi;
    }

    private void SetAppearance(Action<Iptv.IptvSettings> change)
    {
        change(_settings);
        SaveSettings();
        ApplyAppearance();
    }
}
