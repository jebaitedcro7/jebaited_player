using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Ciklus 5, N4: vlastita slika pozadine početnog ekrana. Slika se čita izvan UI dretve i dekodira najviše u širini
/// zaslona (velika fotografija ne troši stotine MB RAM-a i ne usporava pokretanje). Sprema se samo putanja.
/// </summary>
public static class HomeBackground
{
    /// <summary>Vrste slika koje Windows sam dekodira (WIC); ostale se odbijaju porukom.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    public static Stretch ToStretch(string mode) => mode switch
    {
        "fit" => Stretch.Uniform,
        "center" => Stretch.None,
        _ => Stretch.UniformToFill,
    };

    /// <summary>Učitaj sliku (zamrznutu, za UI). Null ako ne postoji ili nije slika.</summary>
    public static Task<BitmapSource?> LoadAsync(string path, int maxPixelWidth) => Task.Run(() => Load(path, maxPixelWidth));

    public static BitmapSource? Load(string path, int maxPixelWidth)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int width = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0].PixelWidth;
            stream.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;

            // Samo smanjivanje: mala slika se ne povećava u memoriji.
            if (maxPixelWidth > 0 && width > maxPixelWidth)
            {
                image.DecodePixelWidth = maxPixelWidth;
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or
                                       InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Log.Warn($"Slika pozadine nije učitana: {ex.Message}");
            return null;
        }
    }
}
