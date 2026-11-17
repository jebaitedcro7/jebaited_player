namespace JebaitedPlayer.Subtitles;

public enum SubtitleFormat
{
    Unknown,
    Srt,
    WebVtt,
    Ass,
    Ssa,
    MicroDvd,
    SubViewer,
    Ttml,
    VobSub,
    Pgs,
}

public static class SubtitleFormats
{
    public static readonly string[] Extensions = [".srt", ".ass", ".ssa", ".sub", ".idx", ".vtt", ".ttml", ".dfxp", ".sup"];

    public static bool IsSubtitleFile(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>VobSub .sub je binarni MPEG-PS (počinje s 00 00 01 BA); MicroDVD .sub je tekst.</summary>
    public static bool IsVobSubData(ReadOnlySpan<byte> head) =>
        head.Length >= 4 && head[0] == 0 && head[1] == 0 && head[2] == 1 && head[3] == 0xBA;

    public static string DisplayName(SubtitleFormat format) => format switch
    {
        SubtitleFormat.Srt => "SRT",
        SubtitleFormat.WebVtt => "WebVTT",
        SubtitleFormat.Ass => "ASS",
        SubtitleFormat.Ssa => "SSA",
        SubtitleFormat.MicroDvd => "MicroDVD",
        SubtitleFormat.SubViewer => "SubViewer",
        SubtitleFormat.Ttml => "TTML",
        SubtitleFormat.VobSub => "VobSub",
        SubtitleFormat.Pgs => "PGS",
        _ => "?",
    };
}

public sealed class SubtitleCue
{
    public long StartMs { get; set; }
    public long EndMs { get; set; }

    /// <summary>Tekst; retci su odvojeni s '\n'. Oznake formata (&lt;i&gt;, {\an8}…) ostaju netaknute.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>ASS/SSA: sva polja Dialogue retka redoslijedom iz "Format:" retka.</summary>
    public string[]? AssFields { get; set; }

    public SubtitleCue Clone() => new()
    {
        StartMs = StartMs,
        EndMs = EndMs,
        Text = Text,
        AssFields = AssFields is null ? null : (string[])AssFields.Clone(),
    };
}

public sealed class SubtitleDocument(SubtitleFormat format)
{
    public SubtitleFormat Format { get; } = format;

    public List<SubtitleCue> Cues { get; } = [];

    /// <summary>ASS/SSA: sve prije događaja, uključujući redak "[Events]".</summary>
    public string? AssHeader { get; set; }

    /// <summary>ASS/SSA: stupci iz "Format:" retka u [Events].</summary>
    public string[]? AssEventFormat { get; set; }

    /// <summary>ASS/SSA: sekcije nakon [Events] (npr. [Fonts]).</summary>
    public string? AssTrailer { get; set; }

    /// <summary>MicroDVD: broj sličica u sekundi korišten za pretvorbu u vrijeme.</summary>
    public double? FrameRate { get; set; }

    public bool IsAss => Format is SubtitleFormat.Ass or SubtitleFormat.Ssa;
}
