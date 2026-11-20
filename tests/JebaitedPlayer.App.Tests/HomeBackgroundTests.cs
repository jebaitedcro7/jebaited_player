using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.App.Tests;

/// <summary>Ciklus 5, N4: slika pozadine početnog ekrana.</summary>
[TestClass]
public sealed class HomeBackgroundTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "jp-homebg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Png(string name, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (40, 80, 160, 255);
        }

        BitmapSource bmp = BitmapSource.Create(width, height, 72, 72, PixelFormats.Bgra32, null, pixels, width * 4);
        string path = Path.Combine(_dir, name);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using FileStream fs = File.Create(path);
        encoder.Save(fs);
        return path;
    }

    [TestMethod]
    public void Large_picture_is_decoded_at_screen_width_and_small_one_is_not_enlarged()
    {
        BitmapSource big = HomeBackground.Load(Png("big.png", 3000, 1500), 1920)!;
        Assert.AreEqual(1920, big.PixelWidth);
        Assert.AreEqual(960, big.PixelHeight);
        Assert.IsTrue(big.IsFrozen, "Slika se koristi na UI dretvi, pa mora biti zamrznuta.");

        BitmapSource small = HomeBackground.Load(Png("small.png", 300, 200), 1920)!;
        Assert.AreEqual(300, small.PixelWidth);
    }

    [TestMethod]
    public void Missing_file_or_not_a_picture_gives_null()
    {
        Assert.IsNull(HomeBackground.Load(Path.Combine(_dir, "nema.jpg"), 1920));
        string text = Path.Combine(_dir, "tekst.jpg");
        File.WriteAllText(text, "nije slika");
        Assert.IsNull(HomeBackground.Load(text, 1920));
    }

    [TestMethod]
    public void Modes_map_to_stretch_and_unknown_values_fall_back_to_fill()
    {
        Assert.AreEqual(Stretch.UniformToFill, HomeBackground.ToStretch("fill"));
        Assert.AreEqual(Stretch.Uniform, HomeBackground.ToStretch("fit"));
        Assert.AreEqual(Stretch.None, HomeBackground.ToStretch("center"));

        AppSettings s = AppSettings.Parse("""{ "HomeBackgroundImage": "  ", "HomeBackgroundMode": "tile" }""");
        Assert.IsNull(s.HomeBackgroundImage);
        Assert.AreEqual("fill", s.HomeBackgroundMode);
    }

    [TestMethod]
    public void Discard_brings_the_picture_back_but_export_and_import_never_carry_the_path()
    {
        var current = new AppSettings { HomeBackgroundImage = @"C:\Slike\a.jpg", HomeBackgroundMode = "fit" };
        string snapshot = SettingsWorkingCopy.Snapshot(current);
        current.HomeBackgroundImage = @"C:\Slike\b.jpg";
        current.HomeBackgroundMode = "center";
        Assert.IsTrue(SettingsWorkingCopy.IsDirty(current, snapshot));
        SettingsWorkingCopy.Revert(current, snapshot);
        Assert.AreEqual(@"C:\Slike\a.jpg", current.HomeBackgroundImage);
        Assert.AreEqual("fit", current.HomeBackgroundMode);

        JsonObject exported = JsonNode.Parse(SettingsTransfer.Export(current, "1.7.4", DateTime.UtcNow))!["settings"]!.AsObject();
        Assert.IsFalse(exported.ContainsKey(nameof(AppSettings.HomeBackgroundImage)));
        Assert.AreEqual("fit", (string?)exported[nameof(AppSettings.HomeBackgroundMode)]);

        var other = new AppSettings { HomeBackgroundImage = @"D:\Moja.jpg" };
        SettingsTransfer.ApplyTo(other, current);
        Assert.AreEqual(@"D:\Moja.jpg", other.HomeBackgroundImage, "Uvoz ne mijenja sliku na ovom računalu.");

        SettingsDefaults.Reset(current, "appearance");
        Assert.IsNull(current.HomeBackgroundImage);
        Assert.AreEqual("fill", current.HomeBackgroundMode);
    }
}
