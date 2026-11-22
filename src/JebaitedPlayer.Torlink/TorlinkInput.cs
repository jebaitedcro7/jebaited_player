using System.Text.RegularExpressions;

namespace JebaitedPlayer.Torlink;

public enum InputKind
{
    Empty,
    Magnet,
    InfoHash,
    TorrentFile,
    Text,
}

/// <summary>Što je korisnik unio u tražilicu: magnet, goli infohash, putanja do .torrent ili običan tekst za pretragu (isto kao u Torlinku).</summary>
public static class TorlinkInput
{
    private static readonly Regex MagnetPattern = new(@"^magnet:\?.*xt=urn:btih:([0-9a-f]{40}|[a-z2-7]{32})(&|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HashPattern = new(@"^([0-9a-f]{40}|[a-z2-7]{32})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static InputKind Classify(string? input, Func<string, bool>? fileExists = null)
    {
        string s = Clean(input);
        if (s.Length == 0)
        {
            return InputKind.Empty;
        }

        if (MagnetPattern.IsMatch(s))
        {
            return InputKind.Magnet;
        }

        if (HashPattern.IsMatch(s))
        {
            return InputKind.InfoHash;
        }

        if (s.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && (fileExists ?? File.Exists)(s))
        {
            return InputKind.TorrentFile;
        }

        return InputKind.Text;
    }

    /// <summary>Povuci-i-ispusti i "Kopiraj kao put" daju navodnike oko putanje.</summary>
    public static string Clean(string? input) => (input ?? string.Empty).Trim().Trim('"').Trim();

    public static string? InfoHashOf(string magnetOrHash)
    {
        string s = Clean(magnetOrHash);
        Match m = MagnetPattern.Match(s);
        if (m.Success)
        {
            return m.Groups[1].Value.ToLowerInvariant();
        }

        return HashPattern.IsMatch(s) ? s.ToLowerInvariant() : null;
    }
}
