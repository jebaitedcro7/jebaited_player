using System.Windows;
using System.Windows.Interop;
using JebaitedPlayer.Infrastructure;
using static JebaitedPlayer.Interop.NativeMethods;

namespace JebaitedPlayer.Views;

/// <summary>
/// Tamna (zadana) ili svijetla tema. Boje su u Themes/Dark.xaml i Themes/Light.xaml s istim ključevima i koriste se
/// kao DynamicResource, pa se tema mijenja odmah, bez ponovnog pokretanja. Kontrole i kartice preko videa
/// uvijek koriste tamne boje (svoj rječnik), jer leže na slici.
/// </summary>
internal static class WindowTheme
{
    public static bool IsDark { get; private set; } = true;

    /// <summary>Tema je promijenjena (prekidač na početnom ekranu i u postavkama ostaju usklađeni).</summary>
    public static event Action? Changed;

    public static string Current => IsDark ? "dark" : "light";

    public static void Apply(string theme)
    {
        bool dark = theme != "light";
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        int index = -1;
        for (int i = 0; i < dictionaries.Count; i++)
        {
            string source = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) || source.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        var replacement = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };
        if (index >= 0)
        {
            dictionaries[index] = replacement;
        }
        else
        {
            dictionaries.Insert(0, replacement);
        }

        IsDark = dark;
        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window);
        }

        // 2.0: vlastita boja isticanja se preračuna za novu temu (u svijetloj tamnija, u tamnoj svjetlija).
        ApplyAccent(Accent);
        Changed?.Invoke();
    }

    /// <summary>2.0: boja isticanja ("" = zadana zelena teme, inače #RRGGBB).</summary>
    public static string Accent { get; private set; } = string.Empty;

    /// <summary>Boja isticanja je promijenjena (mini player preračuna LCD).</summary>
    public static event Action? AccentChanged;

    /// <summary>Izvedene boje za trenutnu temu i boju isticanja.</summary>
    public static AccentPalette Palette => AccentColors.For(Accent, IsDark);

    /// <summary>
    /// Postavlja boju isticanja u svim prozorima odmah (DynamicResource). Zadana boja ne postavlja ništa, pa vrijede
    /// boje iz Dark.xaml / Light.xaml točno kao prije 2.0.
    /// </summary>
    public static void ApplyAccent(string? hex)
    {
        Accent = AccentColors.Normalize(hex);
        ResourceDictionary resources = Application.Current.Resources;
        if (Accent.Length == 0)
        {
            resources.Remove("AccentBrush");
            resources.Remove("AccentForegroundBrush");
            resources.Remove("SelectionBrush");
        }
        else
        {
            AccentPalette p = Palette;
            resources["AccentBrush"] = Frozen(p.Accent);
            resources["AccentForegroundBrush"] = Frozen(p.AccentForeground);
            resources["SelectionBrush"] = Frozen(p.Selection);
        }

        AccentChanged?.Invoke();
    }

    internal static System.Windows.Media.SolidColorBrush Frozen(System.Windows.Media.Color color)
    {
        var brush = new System.Windows.Media.SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Naslovna traka (Windows 11) u boji teme, čim prozor dobije handle.</summary>
    public static void UseDarkTitleBar(Window window) => window.SourceInitialized += (_, _) => ApplyTitleBar(window);

    public static void ApplyTitleBar(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
        {
            return;
        }

        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int caption = IsDark ? 0x00121212 : 0x00F3F3F3; // COLORREF (0x00BBGGRR)
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
    }
}
