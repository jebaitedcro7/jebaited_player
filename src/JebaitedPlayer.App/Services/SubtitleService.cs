using System.Security.Cryptography;
using System.Text;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Subtitles;

namespace JebaitedPlayer.Services;

/// <summary>Titlovi pripremljeni za mpv: originalna datoteka i (za tekstualne titlove) UTF-8 kopija u cacheu.</summary>
public sealed record PreparedSubtitle(
    string OriginalPath,
    string PlaybackPath,
    SubtitleFormat Format,
    string? EncodingName,
    int? CodePage,
    string? Language,
    int CueCount,
    bool IsBitmap,
    IReadOnlyList<JebaitedPlayer.Sync.Cue>? Cues = null)
{
    /// <summary>Vremena replika (s) kakva mpv vidi prije sub-speed/sub-delay; za provjeru prema govoru.</summary>
    public IReadOnlyList<JebaitedPlayer.Sync.Cue> Timings => Cues ?? [];
}

/// <summary>
/// Priprema lokalnih titlova: prepoznavanje encodinga, čitanje formata, jezik i pretvorba u UTF-8.
/// <para>Sigurnost: samo datoteke titlova, ograničena veličina, ništa se ne izvršava, original se ne mijenja.</para>
/// </summary>
public static class SubtitleService
{
    public const long MaxTextSubtitleBytes = 20L * 1024 * 1024;

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static PreparedSubtitle Prepare(string path, string? videoPath, double? videoFps, int? forcedCodePage = null)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException(Loc.T("error.subtitleMissing"), path);
        }

        string ext = info.Extension.ToLowerInvariant();
        string? languageFromName = SubtitleLanguage.FromFileName(path, videoPath);

        // Slikovni titlovi (PGS .sup, VobSub .idx/.sub): mpv ih čita izravno.
        if (ext == ".sup")
        {
            return new PreparedSubtitle(path, path, SubtitleFormat.Pgs, null, null, languageFromName, 0, true);
        }

        if (ext == ".idx")
        {
            return new PreparedSubtitle(path, path, SubtitleFormat.VobSub, null, null, languageFromName, 0, true);
        }

        if (ext == ".sub" && IsVobSub(path))
        {
            string idx = Path.ChangeExtension(path, ".idx");
            if (!File.Exists(idx))
            {
                throw new InvalidDataException(Loc.T("error.vobSubNeedsIdx"));
            }

            return new PreparedSubtitle(path, idx, SubtitleFormat.VobSub, null, null, languageFromName, 0, true);
        }

        if (info.Length > MaxTextSubtitleBytes)
        {
            throw new InvalidDataException(Loc.T("error.subtitleTooLarge"));
        }

        byte[] bytes = File.ReadAllBytes(path);
        Encoding? forced = forcedCodePage is int cp ? SubtitleEncoding.Get(cp) : null;
        string text = SubtitleEncoding.Decode(bytes, forced, out EncodingDetection detection);
        if (LooksBinary(text))
        {
            throw new InvalidDataException(Loc.T("error.notTextSubtitle"));
        }

        SubtitleDocument doc = SubtitleParser.Parse(text, path, videoFps);
        if (doc.Cues.Count == 0)
        {
            throw new InvalidDataException(Loc.T("error.noCuesFound"));
        }

        string? language = languageFromName ??
                           SubtitleLanguage.DetectFromText(string.Join("\n", doc.Cues.Take(500).Select(c => c.Text)));

        var (content, outExt) = SubtitleWriter.ForPlayback(doc);
        Directory.CreateDirectory(AppPaths.SubtitleCacheDir);
        string target = Path.Combine(AppPaths.SubtitleCacheDir,
            CacheKey($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{detection.Encoding.CodePage}|{videoFps}") + outExt);
        File.WriteAllText(target, content, Utf8WithBom);

        return new PreparedSubtitle(info.FullName, target, doc.Format, detection.DisplayName, detection.Encoding.CodePage,
            language, doc.Cues.Count, false, ToCues(doc));
    }

    /// <summary>
    /// Kopija titlova s korekcijom po dijelovima (titlovi za drugo izdanje) za mpv. Original se ne mijenja; izvorna vremena
    /// (<see cref="PreparedSubtitle.Timings"/>) ostaju ista, da se sinkronizacija uvijek može ponoviti od originala.
    /// </summary>
    public static PreparedSubtitle PrepareMapped(PreparedSubtitle prepared, SubtitleTimeMap map, double? videoFps)
    {
        if (prepared.IsBitmap)
        {
            throw new InvalidOperationException(Loc.T("error.bitmapPiecewise"));
        }

        byte[] bytes = File.ReadAllBytes(prepared.OriginalPath);
        Encoding? encoding = prepared.CodePage is int cp ? SubtitleEncoding.Get(cp) : null;
        string text = SubtitleEncoding.Decode(bytes, encoding, out _);
        SubtitleDocument doc = map.ApplyTo(SubtitleParser.Parse(text, prepared.OriginalPath, videoFps));
        var (content, outExt) = SubtitleWriter.ForPlayback(doc);
        Directory.CreateDirectory(AppPaths.SubtitleCacheDir);
        string target = Path.Combine(AppPaths.SubtitleCacheDir,
            Path.GetFileNameWithoutExtension(prepared.PlaybackPath) + "_dijelovi_" + map.Hash() + outExt);
        File.WriteAllText(target, content, Utf8WithBom);
        return prepared with { PlaybackPath = target };
    }

    /// <summary>Vremena replika iz preuzetih titlova (u memoriji, prije spremanja) za provjeru prema govoru.</summary>
    public static IReadOnlyList<JebaitedPlayer.Sync.Cue> ReadCues(byte[] bytes, string fileName, double? videoFps)
    {
        string text = SubtitleEncoding.Decode(bytes, null, out _);
        if (LooksBinary(text))
        {
            throw new InvalidDataException(Loc.T("error.notTextSubtitle"));
        }

        return ToCues(SubtitleParser.Parse(text, fileName, videoFps));
    }

    private static List<JebaitedPlayer.Sync.Cue> ToCues(SubtitleDocument doc) =>
        doc.Cues.Select(c => new JebaitedPlayer.Sync.Cue(c.StartMs / 1000.0, c.EndMs / 1000.0)).ToList();

    /// <summary>Sprema kopiju titlova s primijenjenom sinkronizacijom (UTF-8 s BOM-om). Original ostaje netaknut.</summary>
    public static void SaveSynced(PreparedSubtitle subtitle, SubtitleTiming timing, string targetPath, double? videoFps,
        SubtitleTimeMap? map = null)
    {
        if (subtitle.IsBitmap)
        {
            throw new InvalidOperationException(Loc.T("error.bitmapSave"));
        }

        byte[] bytes = File.ReadAllBytes(subtitle.OriginalPath);
        Encoding? encoding = subtitle.CodePage is int cp ? SubtitleEncoding.Get(cp) : null;
        string text = SubtitleEncoding.Decode(bytes, encoding, out _);
        SubtitleDocument parsed = SubtitleParser.Parse(text, subtitle.OriginalPath, videoFps);

        // Kao u playeru: prvo korekcija po dijelovima (ispravljena kopija), pa pomak i brzina (sub-delay, sub-speed).
        SubtitleDocument doc = timing.ApplyTo(map is null ? parsed : map.ApplyTo(parsed));
        string content = doc.IsAss ? SubtitleWriter.ToAss(doc) : SubtitleWriter.ToSrt(doc);
        File.WriteAllText(targetPath, content, Utf8WithBom);
    }

    /// <summary>Briše stare pretvorene titlove iz cachea.</summary>
    public static void CleanupCache(TimeSpan maxAge)
    {
        try
        {
            if (!Directory.Exists(AppPaths.SubtitleCacheDir))
            {
                return;
            }

            DateTime limit = DateTime.UtcNow - maxAge;
            foreach (string file in Directory.EnumerateFiles(AppPaths.SubtitleCacheDir))
            {
                if (File.GetLastWriteTimeUtc(file) < limit)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Čišćenje cachea titlova: {ex.Message}");
        }
    }

    private static bool IsVobSub(string path)
    {
        Span<byte> head = stackalloc byte[4];
        using FileStream fs = File.OpenRead(path);
        int read = fs.Read(head);
        return SubtitleFormats.IsVobSubData(head[..read]);
    }

    private static bool LooksBinary(string text)
    {
        int sample = Math.Min(text.Length, 20_000);
        if (sample == 0)
        {
            return false;
        }

        int control = 0;
        for (int i = 0; i < sample; i++)
        {
            char c = text[i];
            if (c < 0x20 && c is not ('\r' or '\n' or '\t'))
            {
                control++;
            }
        }

        return control > sample / 100;
    }

    private static string CacheKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();
}
