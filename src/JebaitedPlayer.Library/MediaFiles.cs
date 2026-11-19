using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace JebaitedPlayer.Library;

/// <summary>Prirodno sortiranje: "Epizoda 2" dolazi prije "Epizoda 10".</summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int si = i, sj = j;
                while (si < x.Length && x[si] == '0')
                {
                    si++;
                }

                while (sj < y.Length && y[sj] == '0')
                {
                    sj++;
                }

                int ei = si, ej = sj;
                while (ei < x.Length && char.IsAsciiDigit(x[ei]))
                {
                    ei++;
                }

                while (ej < y.Length && char.IsAsciiDigit(y[ej]))
                {
                    ej++;
                }

                int byLength = (ei - si).CompareTo(ej - sj);
                if (byLength != 0)
                {
                    return byLength;
                }

                int byDigits = string.CompareOrdinal(x, si, y, sj, ei - si);
                if (byDigits != 0)
                {
                    return byDigits;
                }

                i = ei;
                j = ej;
                continue;
            }

            int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (c != 0)
            {
                return c;
            }

            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}

/// <summary>
/// "Otisak" datoteke kakav koristi OpenSubtitles: veličina + zbroj prvih i zadnjih 64 KB kao 64-bitnih brojeva.
/// Čita samo 128 KB, pa je trenutan i za datoteke od 50 GB.
/// </summary>
public static class OpenSubtitlesHash
{
    private const int ChunkSize = 64 * 1024;

    public static string Compute(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1, FileOptions.RandomAccess);
        long length = stream.Length;
        ulong hash = (ulong)length;
        byte[] buffer = new byte[ChunkSize];
        int count = (int)Math.Min(ChunkSize, length);

        hash += SumChunk(stream, 0, buffer, count);
        hash += SumChunk(stream, Math.Max(0, length - ChunkSize), buffer, count);
        return hash.ToString("x16");
    }

    private static ulong SumChunk(FileStream stream, long offset, byte[] buffer, int count)
    {
        stream.Position = offset;
        stream.ReadExactly(buffer, 0, count);
        ulong sum = 0;
        for (int i = 0; i + 8 <= count; i += 8)
        {
            sum += BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(i, 8));
        }

        return sum;
    }
}

public sealed record ScanResult(IReadOnlyList<string> Files, int SkippedSamples, bool Truncated);

/// <summary>Traženje videa u mapama (rekurzivno), preskakanje "sample" datoteka i slaganje serija po sezoni i epizodi.</summary>
public static partial class MediaScanner
{
    public static readonly string[] VideoExtensions =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mpeg", ".mpg", ".m4v", ".ts", ".mts", ".m2ts", ".vob", ".3gp", ".ogv"];

    public static readonly string[] AudioExtensions =
        [".mp3", ".aac", ".flac", ".wav", ".ogg", ".opus", ".ac3", ".dts", ".eac3", ".m4a", ".wma", ".mka"];

    private static readonly string[] SkippedFolders = ["sample", "samples", "$recycle.bin", "system volume information"];

    private const long SampleSizeLimit = 400L * 1024 * 1024;

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static bool IsAudio(string path) => AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static ScanResult ScanFolder(string folder, int maxDepth = 5, int maxFiles = 3000)
    {
        var videos = new List<string>();
        var audio = new List<string>();
        int skipped = 0;
        bool truncated = false;
        var pending = new Stack<(string Dir, int Depth)>();
        pending.Push((folder, 0));

        while (pending.Count > 0 && !truncated)
        {
            var (dir, depth) = pending.Pop();
            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
            {
                if (IsVideo(file))
                {
                    if (IsSample(file))
                    {
                        skipped++;
                        continue;
                    }

                    videos.Add(file);
                }
                else if (IsAudio(file))
                {
                    audio.Add(file);
                }

                if (videos.Count + audio.Count >= maxFiles)
                {
                    truncated = true;
                    break;
                }
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            try
            {
                foreach (string sub in Directory.EnumerateDirectories(dir))
                {
                    var info = new DirectoryInfo(sub);
                    if (SkippedFolders.Contains(info.Name.ToLowerInvariant()) ||
                        info.Attributes.HasFlag(FileAttributes.Hidden) || info.Attributes.HasFlag(FileAttributes.System))
                    {
                        continue;
                    }

                    pending.Push((sub, depth + 1));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return new ScanResult(Sort(videos.Count > 0 ? videos : audio), skipped, truncated);
    }

    /// <summary>Ciklus 2 (mini player): ostale pjesme iz iste mape (album), da se otvaranjem jedne pjesme svira cijeli album.</summary>
    public static IReadOnlyList<string> SiblingAudio(string file)
    {
        string? dir = Path.GetDirectoryName(file);
        if (dir is null)
        {
            return [file];
        }

        try
        {
            var files = Directory.EnumerateFiles(dir).Where(IsAudio).ToList();
            if (!files.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                files.Add(file);
            }

            return Sort(files);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [file];
        }
    }

    /// <summary>Ostale epizode iz iste mape (za "sljedeću epizodu" kad se otvori jedna datoteka).</summary>
    public static IReadOnlyList<string> SiblingVideos(string file)
    {
        string? dir = Path.GetDirectoryName(file);
        if (dir is null)
        {
            return [file];
        }

        try
        {
            var files = Directory.EnumerateFiles(dir).Where(f => IsVideo(f) && !IsSample(f)).ToList();
            if (!files.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                files.Add(file);
            }

            return Sort(files);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [file];
        }
    }

    /// <summary>Epizode po seriji, sezoni i epizodi; ostalo prirodnim redoslijedom.</summary>
    public static IReadOnlyList<string> Sort(IEnumerable<string> files) =>
        files.Select(f => (Path: f, Info: MediaNameParser.Parse(f)))
            .OrderBy(x => x.Info.IsEpisode ? 0 : 1)
            .ThenBy(x => x.Info.IsEpisode ? MediaNameParser.NormalizeTitle(x.Info.Title) : string.Empty, StringComparer.Ordinal)
            .ThenBy(x => x.Info.Season ?? 0)
            .ThenBy(x => x.Info.Episode ?? 0)
            .ThenBy(x => x.Path, NaturalStringComparer.Instance)
            .Select(x => x.Path)
            .ToList();

    private static bool IsSample(string file)
    {
        if (!SampleWord().IsMatch(Path.GetFileNameWithoutExtension(file)))
        {
            return false;
        }

        try
        {
            return new FileInfo(file).Length < SampleSizeLimit;
        }
        catch (IOException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"(^|[\W_])sample([\W_]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SampleWord();
}
