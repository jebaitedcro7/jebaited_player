using System.Reflection;
using System.Text;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Ključ aplikacije za OpenSubtitles (jedan za sve korisnike playera; svaki korisnik se prijavljuje svojim računom,
/// pa se dnevni limit preuzimanja broji po korisniku). Ključ nije u javnom kodu: skripta za izdanje ga upisuje u
/// exe iz lokalne datoteke <c>secrets\opensubtitles-app-key.txt</c>. Razvojna verzija ga nema, pa vodič traži osobni ključ.
/// <para>Ključ je u exe-u samo prikriven (nije tajna od nekog tko rastavlja program), što je uobičajeno za desktop
/// aplikacije; OpenSubtitles limite vodi po korisničkom računu i IP adresi.</para>
/// </summary>
internal static class AppSecrets
{
    private static readonly byte[] Mask = Encoding.ASCII.GetBytes("jebaited_player/opensubtitles");

    public static string? OpenSubtitlesAppKey { get; } = Decode(
        typeof(AppSecrets).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "OsAppKey")?.Value);

    public static bool HasOpenSubtitlesAppKey => OpenSubtitlesAppKey is { Length: > 0 };

    /// <summary>Isti postupak koristi skripta za izdanje (tools\release.ps1).</summary>
    internal static string Encode(string key)
    {
        byte[] b = Encoding.UTF8.GetBytes(key);
        for (int i = 0; i < b.Length; i++)
        {
            b[i] ^= Mask[i % Mask.Length];
        }

        return Convert.ToBase64String(b);
    }

    internal static string? Decode(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            byte[] b = Convert.FromBase64String(encoded);
            for (int i = 0; i < b.Length; i++)
            {
                b[i] ^= Mask[i % Mask.Length];
            }

            string key = Encoding.UTF8.GetString(b).Trim();
            return key.Length > 0 ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
