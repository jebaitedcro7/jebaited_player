using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JebaitedPlayer.Library;

namespace JebaitedPlayer.Services;

/// <summary>
/// 2.1.12: omot trenutne pjesme za mini player i Home ekran. Čita se i dekodira izvan UI dretve, u maloj veličini
/// (256 px, ≈ 256 KB u memoriji), i samo kad se pjesma promijeni. Radio, Spotify, YouTube, filmovi i pjesme bez slike
/// nemaju omot (prikazuje se NLO logo).
/// </summary>
internal sealed class CoverArtLoader(Dispatcher dispatcher)
{
    public const int DecodePixels = 256;

    private string? _requested;
    private int _generation;

    /// <summary>Omot za zadnju traženu pjesmu; null dok se čita ili ako ga nema.</summary>
    public ImageSource? Current { get; private set; }

    /// <summary>Putanja kojoj pripada <see cref="Current"/> (za provjeru).</summary>
    public string? CurrentPath { get; private set; }

    /// <summary>Odakle je omot ("embedded" ili "folder"), za provjeru.</summary>
    public string? CurrentSource { get; private set; }

    /// <summary>Širina dekodirane slike u pikselima (za provjeru).</summary>
    public int CurrentPixelWidth { get; private set; }

    /// <summary>Omot je učitan (ili je sigurno da ga nema); poziva se na UI dretvi.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Traži omot za ovu pjesmu (UI dretva, smije se zvati često: radi samo kad se putanja promijeni).
    /// Null ili nešto što nije lokalna glazbena datoteka briše omot.
    /// </summary>
    public void Request(string? path)
    {
        string? key = IsEligible(path) ? path : null;
        if (string.Equals(key, _requested, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _requested = key;
        int generation = ++_generation;
        if (Current is not null || CurrentPath is not null)
        {
            Current = null;
            CurrentPath = null;
            CurrentSource = null;
            CurrentPixelWidth = 0;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        if (key is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            CoverArtImage? art = CoverArt.Read(key);
            BitmapSource? image = art is null ? null : Decode(art.Data);
            dispatcher.BeginInvoke(() =>
            {
                if (generation != _generation)
                {
                    return; // u međuvremenu je zatražena druga pjesma
                }

                Current = image;
                CurrentPath = key;
                CurrentSource = image is null ? null : art!.Source;
                CurrentPixelWidth = image?.PixelWidth ?? 0;
                Changed?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    /// <summary>Lokalna glazbena datoteka (radio i drugi URL-ovi nemaju omot iz oznaka).</summary>
    internal static bool IsEligible(string? path) =>
        !string.IsNullOrEmpty(path) && System.IO.Path.IsPathFullyQualified(path) && !path.Contains("://", StringComparison.Ordinal) &&
        MediaScanner.IsAudio(path);

    /// <summary>Mala, zamrznuta slika (može se koristiti s bilo koje dretve); null ako se ne da dekodirati.</summary>
    internal static BitmapSource? Decode(byte[] data)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = DecodePixels;
            image.StreamSource = new MemoryStream(data, writable: false);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or IOException
                                       or System.Runtime.InteropServices.COMException or OverflowException)
        {
            return null;
        }
    }
}
