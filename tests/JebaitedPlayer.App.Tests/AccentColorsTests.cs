using System.IO;
using System.Windows.Media;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.App.Tests;

/// <summary>2.0: boja isticanja – zadana ista kao prije, čitljivost u obje teme, spremanje.</summary>
[TestClass]
public sealed class AccentColorsTests
{
    private static Color C(string hex)
    {
        Assert.IsTrue(AccentColors.TryParse(hex, out Color c), hex);
        return c;
    }

    [TestMethod]
    public void Default_green_gives_exactly_the_colors_used_before_2_0()
    {
        AccentPalette dark = AccentColors.For(string.Empty, dark: true);
        Assert.AreEqual(C("#7BD88F"), dark.Accent);
        Assert.AreEqual(C("#0B1A0F"), dark.AccentForeground);
        Assert.AreEqual(C("#2E4A35"), dark.Selection);
        Assert.AreEqual(C("#7BD88F"), dark.LcdText);
        Assert.AreEqual(C("#2A4A32"), dark.LcdBorder);
        Assert.AreEqual(C("#17291C"), dark.LcdTrack);
        Assert.AreEqual(C("#0B100C"), dark.LcdBack);
        Assert.AreEqual(C("#1F3324"), dark.LcdTag);
        Assert.AreEqual(C("#223A28"), dark.LcdDim);

        AccentPalette light = AccentColors.For(null, dark: false);
        // 2.1.12: svijetla tema dobila je malo tamniju zelenu (prije #1F8A3B, 4,41:1 uz bijeli tekst).
        Assert.AreEqual(C("#1E8739"), light.Accent);
        Assert.AreEqual(Colors.White, light.AccentForeground);
        Assert.AreEqual(C("#D3EEDA"), light.Selection);

        Assert.AreEqual(dark, AccentColors.For("#7bd88f", dark: true), "the green preset is the default");
    }

    [TestMethod]
    public void Light_theme_default_green_matches_Light_xaml_and_is_readable()
    {
        string xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "JebaitedPlayer.App", "Themes", "Light.xaml"));
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(xaml, "x:Key=\"AccentBrush\" Color=\"(#[0-9A-Fa-f]{6})\"");
        Assert.IsTrue(m.Success, "AccentBrush in Light.xaml");
        AccentPalette light = AccentColors.For(null, dark: false);
        Assert.AreEqual(C(m.Groups[1].Value), light.Accent, "AccentColors and Light.xaml must use the same green");
        Assert.IsTrue(AccentColors.Contrast(Colors.White, light.Accent) >= 4.5, $"white text {AccentColors.Contrast(Colors.White, light.Accent):F2}");
        Assert.IsTrue(AccentColors.Contrast(light.Accent, C("#F3F3F3")) >= 3, "accent vs light background");
    }

    [TestMethod]
    public void Every_shipped_component_has_its_license_text()
    {
        string root = RepoRoot();
        string licenses = Path.Combine(root, "third_party", "licenses");
        string[] files = Directory.GetFiles(licenses, "*.txt").Select(Path.GetFileName).ToArray()!;
        // Komponente iz tablice u THIRD_PARTY_NOTICES.md (osim libmpv/FFmpeg, čiji je izvor opisan u third_party/libmpv/SOURCE.md).
        string[] required =
        [
            "dotnet-runtime-and-WPF-MIT.txt", "dotnet-runtime-THIRD-PARTY-NOTICES.txt", "Velopack-MIT.txt", "Microsoft.Data.Sqlite-MIT.txt",
            "SQLitePCLRaw-Apache-2.0.txt", "SQLite-public-domain.txt", "WebRtcVadSharp-MIT-and-WebRTC-BSD-3-Clause.txt",
            "WebView2-SDK-BSD-3-Clause.txt", "NetCord-MIT.txt", "Concentus-BSD-3-Clause.txt", "libdave-MIT.txt",
            "OpenSSL-Apache-2.0.txt", "mlspp-BSD-2-Clause.txt", "nlohmann-json-MIT.txt",
        ];
        foreach (string name in required)
        {
            Assert.IsTrue(files.Contains(name), $"missing third_party/licenses/{name}");
            Assert.IsTrue(new FileInfo(Path.Combine(licenses, name)).Length > 300, $"{name} looks empty");
        }

        string notices = File.ReadAllText(Path.Combine(root, "THIRD_PARTY_NOTICES.md"));
        foreach (string component in new[] { "Velopack", "SQLitePCLRaw", "WebRtcVadSharp", "WebView2", "Microsoft.Data.Sqlite" })
        {
            Assert.IsTrue(notices.Contains(component, StringComparison.Ordinal), component);
        }

        string csproj = File.ReadAllText(Path.Combine(root, "src", "JebaitedPlayer.App", "JebaitedPlayer.App.csproj"));
        StringAssert.Contains(csproj, @"third_party\licenses\*.txt", "licenses are copied next to the exe");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "THIRD_PARTY_NOTICES.md")))
        {
            dir = dir.Parent;
        }

        Assert.IsNotNull(dir, "repo root");
        return dir.FullName;
    }

    [TestMethod]
    public void Saved_value_is_normalized()
    {
        Assert.AreEqual(string.Empty, AccentColors.Normalize("#7BD88F"));
        Assert.AreEqual(string.Empty, AccentColors.Normalize(null));
        Assert.AreEqual("#6CB4FF", AccentColors.Normalize("#6cb4ff"));
        Assert.AreEqual("#6CB4FF", AccentColors.Normalize(" 6CB4FF "));
        foreach (string bad in new[] { "#12345", "blue", "#GGGGGG", "#1234567", "rgb(1,2,3)" })
        {
            Assert.AreEqual(string.Empty, AccentColors.Normalize(bad), bad);
        }
    }

    [TestMethod]
    public void Every_preset_and_extreme_colors_stay_readable_in_both_themes()
    {
        var colors = AccentColors.Presets.Select(p => p.Hex)
            .Concat(["#000000", "#FFFFFF", "#101010", "#FFFF00", "#0000FF", "#00FF00", "#FF0000", "#808080", "#3A3A3A"]);
        Color darkBackground = C("#121212"), lightBackground = C("#F3F3F3");
        foreach (string hex in colors)
        {
            foreach (bool dark in new[] { true, false })
            {
                AccentPalette p = AccentColors.For(hex, dark);
                Color background = dark ? darkBackground : lightBackground;
                Assert.IsTrue(AccentColors.Contrast(p.Accent, background) >= 3 - 1e-9, $"{hex} {(dark ? "dark" : "light")}: accent {AccentColors.Contrast(p.Accent, background):F2}");
                // 2.1.12: i zadana zelena u svijetloj temi sad ima ≥ 4,5:1 (prije iznimka s 4,41:1).
                Assert.IsTrue(AccentColors.Contrast(p.AccentForeground, p.Accent) >= 4.5 - 1e-9, $"{hex} {(dark ? "dark" : "light")}: text {AccentColors.Contrast(p.AccentForeground, p.Accent):F2}");
                Assert.IsTrue(AccentColors.Contrast(p.LcdText, p.LcdBack) >= 4.5 - 1e-9, $"{hex}: LCD {AccentColors.Contrast(p.LcdText, p.LcdBack):F2}");
            }
        }
    }

    [TestMethod]
    public void A_readable_color_is_used_as_it_is()
    {
        // Plava je dovoljno svijetla na tamnoj pozadini: ne mijenja se; na svijetloj se potamni.
        AccentPalette dark = AccentColors.For("#6CB4FF", dark: true);
        Assert.AreEqual(C("#6CB4FF"), dark.Accent);
        AccentPalette light = AccentColors.For("#6CB4FF", dark: false);
        Assert.AreNotEqual(C("#6CB4FF"), light.Accent);
        Assert.IsTrue(AccentColors.Luminance(light.Accent) < AccentColors.Luminance(C("#6CB4FF")));
    }

    [TestMethod]
    public void Hsl_round_trip_is_close()
    {
        foreach (string hex in AccentColors.Presets.Select(p => p.Hex).Concat(["#123456", "#FEDCBA", "#000000", "#FFFFFF"]))
        {
            Color c = C(hex);
            (double h, double s, double l) = AccentColors.ToHsl(c);
            Color back = AccentColors.FromHsl(h, s, l);
            Assert.IsTrue(Math.Abs(back.R - c.R) <= 1 && Math.Abs(back.G - c.G) <= 1 && Math.Abs(back.B - c.B) <= 1, $"{hex} → {AccentColors.ToHex(back)}");
        }
    }

    [TestMethod]
    public void Accent_is_saved_in_settings_and_reset_with_the_appearance_tab()
    {
        AppSettings s = AppSettings.Parse("""{ "AccentColor": "#ff7a7a" }""");
        Assert.AreEqual("#FF7A7A", s.AccentColor);
        Assert.AreEqual(string.Empty, AppSettings.Parse("""{ "AccentColor": "nonsense" }""").AccentColor);
        Assert.AreEqual(string.Empty, new AppSettings().AccentColor);

        SettingsDefaults.Reset(s, "appearance");
        Assert.AreEqual(string.Empty, s.AccentColor);
    }
}
