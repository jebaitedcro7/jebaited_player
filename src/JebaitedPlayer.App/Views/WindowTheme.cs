using System.Windows;
using System.Windows.Interop;
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
