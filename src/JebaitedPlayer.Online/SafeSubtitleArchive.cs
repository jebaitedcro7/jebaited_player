using System.IO.Compression;
using JebaitedPlayer.Library;
using JebaitedPlayer.Subtitles;

namespace JebaitedPlayer.Online;

/// <summary>
/// Sigurno otvaranje preuzetih titlova: ZIP se čita samo u memoriji, uzimaju se samo datoteke titlova,
/// s ograničenjem broja i veličine (zaštita od "zip bombi") i bez putanja iz arhive (zaštita od zip-slip).
/// Ništa se ne izvršava i ništa se ne raspakirava na disk.
/// </summary>
public static class SafeSubtitleArchive
{
    public const long MaxSubtitleBytes = 10L * 1024 * 1024;
    public const long MaxTotalBytes = 40L * 1024 * 1024;
    public const int MaxEntries = 200;
    public const int MaxCompressionRatio = 100;

    public static DownloadedSubtitle ExtractBest(byte[] data, string fallbackName, MediaInfo info)
    {
        if (IsRar(data))
        {
            throw new ProviderException(Loc.T("online.archive.rar"));
        }

        if (!IsZip(data))
        {
            if (data.Length > MaxSubtitleBytes)
            {
                throw new ProviderException(Loc.T("online.archive.fileTooLarge"));
            }

            return new DownloadedSubtitle(SafeName(fallbackName), data);
        }

        using var zip = new ZipArchive(new MemoryStream(data, writable: false), ZipArchiveMode.Read);
        if (zip.Entries.Count > MaxEntries)
        {
            throw new ProviderException(Loc.T("online.archive.tooManyFiles"));
        }

        long total = 0;
        var subtitles = new List<(ZipArchiveEntry Entry, string Name)>();
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = SafeName(entry.FullName);
            if (entry.FullName.EndsWith('/') || entry.Length == 0 || name.StartsWith("._", StringComparison.Ordinal) ||
                entry.FullName.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase) || !SubtitleFormats.IsSubtitleFile(name) ||
                name.EndsWith(".idx", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.Length > MaxSubtitleBytes)
            {
                throw new ProviderException(Loc.T("online.archive.entryTooLarge"));
            }

            if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaxCompressionRatio)
            {
                throw new ProviderException(Loc.T("online.archive.suspicious"));
            }

            total += entry.Length;
            if (total > MaxTotalBytes)
            {
                throw new ProviderException(Loc.T("online.archive.tooLarge"));
            }

            subtitles.Add((entry, name));
        }

        if (subtitles.Count == 0)
        {
            throw new ProviderException(Loc.T("online.archive.noSubtitles"));
        }

        var chosen = subtitles
            .OrderByDescending(s => EpisodeMatches(s.Name, info))
            .ThenBy(s => ExtensionRank(s.Name))
            .ThenByDescending(s => s.Entry.Length)
            .First();

        using Stream stream = chosen.Entry.Open();
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxSubtitleBytes)
            {
                throw new ProviderException(Loc.T("online.archive.entryTooLarge"));
            }

            buffer.Write(chunk, 0, read);
        }

        return new DownloadedSubtitle(chosen.Name, buffer.ToArray());
    }

    public static bool IsZip(byte[] d) => d.Length >= 4 && d[0] == 'P' && d[1] == 'K' && d[2] is 3 or 5 or 7 && d[3] is 4 or 6 or 8;

    public static bool IsRar(byte[] d) => d.Length >= 4 && d[0] == 'R' && d[1] == 'a' && d[2] == 'r' && d[3] == '!';

    /// <summary>Samo ime datoteke, bez mapa i nedopuštenih znakova.</summary>
    public static string SafeName(string name)
    {
        string file = Path.GetFileName(name.Replace('\\', '/').Split('/').Last());
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            file = file.Replace(c, '_');
        }

        return file.Length == 0 ? "titlovi.srt" : file;
    }

    private static bool EpisodeMatches(string fileName, MediaInfo info)
    {
        if (!info.IsEpisode)
        {
            return false;
        }

        MediaInfo f = MediaNameParser.Parse(fileName);
        return f.Episode == info.Episode && (f.Season is null || info.Season is null || f.Season == info.Season);
    }

    private static int ExtensionRank(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".srt" => 0,
        ".ass" or ".ssa" => 1,
        ".vtt" => 2,
        _ => 3,
    };
}
