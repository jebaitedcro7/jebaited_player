using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JebaitedPlayer.Subtitles;

/// <summary>Zapisivanje titlova u SRT ili ASS/SSA (UTF-8).</summary>
public static partial class SubtitleWriter
{
    private const string DefaultAssHeader =
        "[Script Info]\nScriptType: v4.00+\nPlayResX: 1920\nPlayResY: 1080\n\n" +
        "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
        "Style: Default,Arial,60,&H00FFFFFF,&H000000FF,&H00000000,&H64000000,0,0,0,0,100,100,0,0,1,3,1,2,40,40,50,1\n\n" +
        "[Events]\n";

    /// <summary>Sadržaj i ekstenzija datoteke koju predajemo mpv-u: ASS/SSA ostaju svoj format, sve ostalo postaje SRT.</summary>
    public static (string Content, string Extension) ForPlayback(SubtitleDocument doc) =>
        doc.IsAss
            ? (ToAss(doc), doc.Format == SubtitleFormat.Ssa ? ".ssa" : ".ass")
            : (ToSrt(doc), ".srt");

    public static string ToSrt(SubtitleDocument doc)
    {
        var sb = new StringBuilder();
        int index = 1;
        foreach (SubtitleCue cue in doc.Cues.OrderBy(c => c.StartMs))
        {
            string text = doc.IsAss ? AssToPlain(cue.Text) : cue.Text;
            if (text.Trim().Length == 0)
            {
                continue;
            }

            long start = Math.Max(0, cue.StartMs);
            long end = Math.Max(start, cue.EndMs);
            sb.Append(index++.ToString(CultureInfo.InvariantCulture)).Append("\r\n")
              .Append(FormatSrtTime(start)).Append(" --> ").Append(FormatSrtTime(end)).Append("\r\n")
              .Append(text.Replace("\n", "\r\n")).Append("\r\n\r\n");
        }

        return sb.ToString();
    }

    public static string ToAss(SubtitleDocument doc)
    {
        string[] format = doc.AssEventFormat ?? SubtitleParser.DefaultFormatFor(doc.Format);
        int startIndex = IndexOf(format, "Start");
        int endIndex = IndexOf(format, "End");
        int textIndex = IndexOf(format, "Text");

        var sb = new StringBuilder();
        string header = doc.AssHeader ?? DefaultAssHeader;
        sb.Append(header);
        if (!header.EndsWith('\n'))
        {
            sb.Append('\n');
        }

        sb.Append("Format: ").Append(string.Join(", ", format)).Append('\n');
        foreach (SubtitleCue cue in doc.Cues)
        {
            string[] fields = new string[format.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i] = cue.AssFields is { } f && i < f.Length ? f[i] : DefaultField(format[i]);
            }

            long start = Math.Max(0, cue.StartMs);
            long end = Math.Max(start, cue.EndMs);
            if (startIndex >= 0)
            {
                fields[startIndex] = FormatAssTime(start);
            }

            if (endIndex >= 0)
            {
                fields[endIndex] = FormatAssTime(end);
            }

            if (textIndex >= 0)
            {
                fields[textIndex] = cue.Text.Replace("\n", "\\N");
            }

            sb.Append("Dialogue: ").Append(string.Join(",", fields)).Append('\n');
        }

        if (doc.AssTrailer != null)
        {
            sb.Append('\n').Append(doc.AssTrailer);
        }

        return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    public static string FormatSrtTime(long ms)
    {
        ms = Math.Max(0, ms);
        long h = ms / 3_600_000;
        long m = ms / 60_000 % 60;
        long s = ms / 1000 % 60;
        long f = ms % 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{h:00}:{m:00}:{s:00},{f:000}");
    }

    public static string FormatAssTime(long ms)
    {
        long cs = (Math.Max(0, ms) + 5) / 10;
        long h = cs / 360_000;
        long m = cs / 6000 % 60;
        long s = cs / 100 % 60;
        long c = cs % 100;
        return string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}.{c:00}");
    }

    private static string AssToPlain(string text) =>
        AssOverride().Replace(text, string.Empty).Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " ");

    private static string DefaultField(string column) => column.ToLowerInvariant() switch
    {
        "layer" or "marked" or "marginl" or "marginr" or "marginv" => "0",
        "style" => "Default",
        _ => string.Empty,
    };

    private static int IndexOf(string[] format, string name) =>
        Array.FindIndex(format, f => f.Equals(name, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\{[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex AssOverride();
}
