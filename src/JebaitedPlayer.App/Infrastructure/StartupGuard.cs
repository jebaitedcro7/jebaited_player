using System.Text.Json;
using System.Windows;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// 2.1.12: čuvar pokretanja. U 2.1.7 je iznimka u Show() ostavila proces živ bez ijednog prozora; taj proces je držao
/// „jednu instancu”, pa svako sljedeće pokretanje nije radilo ništa. Ovdje su dva dijela:
/// <list type="bullet">
/// <item>oznaka u mapi podataka: upiše se pri pokretanju i briše čim je prozor playera stvarno na zaslonu. Ako je
/// pri sljedećem pokretanju još tu, prošlo pokretanje nije došlo do prozora, pa se player pokreće sigurno
/// (normalan glavni prozor na sredini, mini se ne otvara sam);</item>
/// <item>provjera „ima li igdje prozora playera” (<see cref="IsUsable"/>), koju MainWindow koristi kao čuvar.</item>
/// </list>
/// </summary>
internal static class StartupGuard
{
    public const string FileName = "startup-pending.json";

    /// <summary>Najmanji vidljivi dio prozora na nekom zaslonu (fizički pikseli) da bi ga korisnik mogao uhvatiti.</summary>
    public const int MinVisiblePixels = 40;

    /// <summary>
    /// Pokretanje počinje: vraća true ako prošlo pokretanje nije došlo do prozora (oznaka je ostala), pa treba sigurno
    /// pokretanje. Greške pri čitanju i pisanju se zanemaruju (čuvar nikad ne smije spriječiti start).
    /// </summary>
    public static bool Begin(string dataDir, string version, DateTime nowUtc)
    {
        string path = Path.Combine(dataDir, FileName);
        bool previousFailed;
        try
        {
            previousFailed = File.Exists(path);
        }
        catch (Exception)
        {
            previousFailed = false;
        }

        try
        {
            Directory.CreateDirectory(dataDir);
            string json = JsonSerializer.Serialize(new { version, startedUtc = nowUtc.ToString("O"), pid = Environment.ProcessId });
            File.WriteAllText(path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Čuvar pokretanja: oznaka nije upisana ({ex.Message}).");
        }

        return previousFailed;
    }

    /// <summary>Prozor playera je na zaslonu: oznaka se briše (može se zvati više puta).</summary>
    public static bool Complete(string dataDir)
    {
        string path = Path.Combine(dataDir, FileName);
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Čuvar pokretanja: oznaka nije obrisana ({ex.Message}).");
            return false;
        }
    }

    /// <summary>
    /// Sigurno pokretanje: samo položaj i način prikaza (glavni prozor normalan i na sredini, mini se ne otvara sam).
    /// Sve ostale postavke ostaju kakve jesu.
    /// </summary>
    public static void ApplySafeStart(AppSettings settings)
    {
        settings.WindowMaximized = false;
        settings.WindowLeft = null;
        settings.WindowTop = null;
        settings.MiniPlayer.WasOpen = false;
    }

    /// <summary>
    /// Može li korisnik ovaj prozor vidjeti i uhvatiti: vidljiv, nije proziran i barem malo je na nekom zaslonu.
    /// Minimiziran prozor s gumbom na taskbaru se računa (korisnik ga vraća klikom).
    /// </summary>
    /// <param name="window">Okvir prozora u fizičkim pikselima.</param>
    /// <param name="virtualScreen">Svi zasloni zajedno u fizičkim pikselima.</param>
    public static bool IsUsable(Rect window, Rect virtualScreen, bool visible, bool minimized, bool inTaskbar, double opacity)
    {
        if (!visible || opacity <= 0.05)
        {
            return false;
        }

        if (minimized)
        {
            return inTaskbar;
        }

        if (window.IsEmpty || virtualScreen.IsEmpty)
        {
            return false;
        }

        Rect onScreen = Rect.Intersect(window, virtualScreen);
        return !onScreen.IsEmpty && onScreen.Width >= MinVisiblePixels && onScreen.Height >= MinVisiblePixels;
    }
}
