using System.Text.RegularExpressions;

namespace JebaitedPlayer.Diagnostics;

/// <summary>Uklanja osobne podatke iz teksta prije zapisa: korisnički profil u putanjama i tajne (ključevi, lozinke, tokeni).</summary>
public static partial class Redactor
{
    // Mapa putanje (disk ili UNC) do zadnje kose crte; ostaje samo naziv datoteke.
    [GeneratedRegex(@"(?:[A-Za-z]:|\\\\[^\\/:*?""<>|\r\n]+\\[^\\/:*?""<>|\r\n]+)\\(?:[^\\/:*?""<>|\r\n]+\\)*")]
    private static partial Regex DirectoryPrefix();

    [GeneratedRegex(@"(?i)\b(api[-_ ]?key|apikey|token|password|passwd|secret|authorization|bearer)\b(\s*[:=]\s*|\s+)(""[^""]*""|'[^']*'|[^\s,;&""']+)")]
    private static partial Regex Secret();

    [GeneratedRegex(@"(?i)([?&](?:api[-_]?key|key|token|password|auth)=)[^&\s]+")]
    private static partial Regex QuerySecret();

    // IPTV (Xtream): vjerodajnice su u putanji /live/KORISNIK/LOZINKA/ID.ts i u upitu username=.
    [GeneratedRegex(@"(?i)(/(?:live|movie|series|timeshift)/)[^/\s]+/[^/\s]+/")]
    private static partial Regex XtreamPath();

    [GeneratedRegex(@"(?i)([?&]username=)[^&\s]+")]
    private static partial Regex QueryUser();

    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string s = XtreamPath().Replace(text, "$1***/***/");
        s = QueryUser().Replace(s, "$1***");
        s = DirectoryPrefix().Replace(s, @"<dir>\");
        s = QuerySecret().Replace(s, "$1***");
        s = Secret().Replace(s, m => $"{m.Groups[1].Value}{m.Groups[2].Value}***");
        return s;
    }

    /// <summary>Samo naziv datoteke (bez mape). Za dijagnostiku formata dovoljan je naziv, a mapa često sadrži osobne podatke.</summary>
    public static string FileName(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFileName(path.TrimEnd('\\', '/'));
        }
        catch (ArgumentException)
        {
            return "?";
        }
    }
}
