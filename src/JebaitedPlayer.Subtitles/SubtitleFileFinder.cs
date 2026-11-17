namespace JebaitedPlayer.Subtitles;

/// <summary>Traži lokalne titlove koji pripadaju videu (isti naziv, ili mapa Subs/Titlovi uz video).</summary>
public static class SubtitleFileFinder
{
    private static readonly string[] SubFolderNames = ["subs", "sub", "subtitles", "titlovi", "titl"];

    private static readonly string[] VideoExtensions =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mpeg", ".mpg", ".m4v", ".ts", ".mts", ".m2ts", ".vob"];

    public static IReadOnlyList<string> FindForVideo(string videoPath, int maxResults = 30)
    {
        string? dir = Path.GetDirectoryName(videoPath);
        if (dir is null || !Directory.Exists(dir))
        {
            return [];
        }

        string baseName = Path.GetFileNameWithoutExtension(videoPath);
        var results = new List<string>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(dir))
            {
                if (SubtitleFormats.IsSubtitleFile(file) && MatchesBase(Path.GetFileName(file), baseName))
                {
                    results.Add(file);
                }
            }

            bool singleVideo = Directory.EnumerateFiles(dir).Count(IsVideoFile) == 1;
            foreach (string sub in Directory.EnumerateDirectories(dir))
            {
                if (!SubFolderNames.Contains(Path.GetFileName(sub).ToLowerInvariant()))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(sub))
                {
                    if (SubtitleFormats.IsSubtitleFile(file) && (singleVideo || MatchesBase(Path.GetFileName(file), baseName)))
                    {
                        results.Add(file);
                    }
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        // VobSub: .idx i .sub idu u paru; mpv-u predajemo .idx.
        results.RemoveAll(f => f.EndsWith(".sub", StringComparison.OrdinalIgnoreCase) &&
                               results.Any(o => o.EndsWith(".idx", StringComparison.OrdinalIgnoreCase) &&
                                                Path.ChangeExtension(o, ".sub").Equals(f, StringComparison.OrdinalIgnoreCase)));

        return results.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    public static bool IsVideoFile(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private static bool MatchesBase(string fileName, string baseName) =>
        fileName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase) &&
        fileName.Length > baseName.Length &&
        ".-_[".Contains(fileName[baseName.Length]);
}
