using JebaitedPlayer.Online;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Spremanje i stanje izvora titlova za vodič "Titlovi s interneta" (bez sučelja, da se može testirati).
/// Sve lozinke i ključevi idu kriptirani (DPAPI) u <see cref="OnlineSettings"/>.
/// </summary>
internal static class OnlineSetup
{
    /// <summary>OpenSubtitles ključ: osobni (ako ga je korisnik upisao) ili ključ aplikacije iz izdanja.</summary>
    public static string? OpenSubtitlesKey(OnlineSettings o) =>
        Secret.Unprotect(o.OpenSubtitlesApiKey) is { Length: > 0 } personal ? personal : AppSecrets.OpenSubtitlesAppKey;

    public static OpenSubtitlesCredentials? OpenSubtitlesCredentials(OnlineSettings o) =>
        OpenSubtitlesKey(o) is { } key
            ? new OpenSubtitlesCredentials(key, o.OpenSubtitlesUsername, Secret.Unprotect(o.OpenSubtitlesPassword))
            : null;

    /// <summary>Korisnik je postavio barem jedan svoj račun ili ključ (inače mu se nudi vodič).</summary>
    public static bool HasPersonalSetup(OnlineSettings o) =>
        (o.OpenSubtitlesEnabled && !string.IsNullOrWhiteSpace(o.OpenSubtitlesUsername) && OpenSubtitlesKey(o) != null) ||
        (o.SubDlEnabled && Secret.Unprotect(o.SubDlApiKey) is { Length: > 0 }) ||
        (o.TitloviEnabled && !string.IsNullOrWhiteSpace(o.TitloviUsername));

    public static void SaveOpenSubtitles(OnlineSettings o, string username, string password, string? personalKey)
    {
        o.OpenSubtitlesEnabled = true;
        o.OpenSubtitlesUsername = username.Trim();
        o.OpenSubtitlesPassword = Secret.Protect(password);
        if (!string.IsNullOrWhiteSpace(personalKey))
        {
            o.OpenSubtitlesApiKey = Secret.Protect(personalKey.Trim());
        }
    }

    public static void SaveSubDl(OnlineSettings o, string key)
    {
        o.SubDlEnabled = true;
        o.SubDlApiKey = Secret.Protect(key.Trim());
    }

    public static void SaveTitlovi(OnlineSettings o, string username, string password)
    {
        o.TitloviEnabled = true;
        o.TitloviUsername = username.Trim();
        o.TitloviPassword = Secret.Protect(password);
    }

    /// <summary>Ključ zalijepljen iz preglednika: bez razmaka, navodnika i novih redaka oko njega.</summary>
    public static string CleanPastedKey(string? text) =>
        (text ?? string.Empty).Trim().Trim('"', '\'', '`').Trim();

    /// <summary>Sažetak za zadnju stranicu vodiča.</summary>
    public static string Summary(OnlineSettings o)
    {
        var lines = new List<string>
        {
            !string.IsNullOrWhiteSpace(o.OpenSubtitlesUsername) && OpenSubtitlesKey(o) != null
                ? $"✓ OpenSubtitles: prijavljen kao {o.OpenSubtitlesUsername}"
                : OpenSubtitlesKey(o) != null
                    ? "– OpenSubtitles: bez računa (najviše 5 preuzimanja dnevno)"
                    : "– OpenSubtitles: nije postavljen",
            Secret.Unprotect(o.SubDlApiKey) is { Length: > 0 } ? "✓ SubDL: ključ spremljen" : "– SubDL: nije postavljen",
            o.TitloviEnabled && !string.IsNullOrWhiteSpace(o.TitloviUsername)
                ? $"✓ Titlovi.com: prijavljen kao {o.TitloviUsername}"
                : "– Titlovi.com: nije postavljen",
        };
        string tail = HasPersonalSetup(o)
            ? "Kad uz video nema hrvatskih titlova, player će ih sam potražiti."
            : "Nijedan račun nije postavljen. Vodič možeš otvoriti kad god želiš.";
        return string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine + tail;
    }
}
