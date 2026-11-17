using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace JebaitedPlayer.Subtitles;

/// <summary>Čitanje tekstualnih titlova: SRT, WebVTT, ASS/SSA, MicroDVD, SubViewer i TTML/DFXP.</summary>
public static partial class SubtitleParser
{
    public const double DefaultFrameRate = 24000.0 / 1001.0;

    private static readonly string[] DefaultAssFormat =
        ["Layer", "Start", "End", "Style", "Name", "MarginL", "MarginR", "MarginV", "Effect", "Text"];

    private static readonly string[] DefaultSsaFormat =
        ["Marked", "Start", "End", "Style", "Name", "MarginL", "MarginR", "MarginV", "Effect", "Text"];

    public static SubtitleDocument Parse(string text, string? fileName = null, double? frameRate = null)
    {
        text = Normalize(text);
        SubtitleFormat format = DetectFormat(text, fileName);
        return format switch
        {
            SubtitleFormat.WebVtt => ParseVtt(text),
            SubtitleFormat.Ass or SubtitleFormat.Ssa => ParseAss(text, format),
            SubtitleFormat.MicroDvd => ParseMicroDvd(text, frameRate),
            SubtitleFormat.SubViewer => ParseSubViewer(text),
            SubtitleFormat.Ttml => ParseTtml(text),
            _ => ParseSrt(text),
        };
    }

    public static SubtitleFormat DetectFormat(string text, string? fileName = null)
    {
        text = Normalize(text);
        string head = text.Length > 8000 ? text[..8000] : text;
        string trimmed = head.TrimStart();

        if (trimmed.StartsWith("WEBVTT", StringComparison.Ordinal))
        {
            return SubtitleFormat.WebVtt;
        }

        if (Has(head, "[Script Info]") || Has(head, "[V4+ Styles]") || Has(head, "[V4 Styles]") || Has(head, "[Events]"))
        {
            return Has(head, "[V4 Styles]") && !Has(head, "[V4+ Styles]") ? SubtitleFormat.Ssa : SubtitleFormat.Ass;
        }

        if (trimmed.StartsWith("<?xml", StringComparison.Ordinal) || trimmed.StartsWith("<tt", StringComparison.Ordinal))
        {
            return SubtitleFormat.Ttml;
        }

        if (MicroDvdLine().IsMatch(FirstLine(trimmed)))
        {
            return SubtitleFormat.MicroDvd;
        }

        if (SrtTime().IsMatch(head))
        {
            return SubtitleFormat.Srt;
        }

        if (Has(head, "[INFORMATION]") || SubViewerTime().IsMatch(head))
        {
            return SubtitleFormat.SubViewer;
        }

        return Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".vtt" => SubtitleFormat.WebVtt,
            ".ass" => SubtitleFormat.Ass,
            ".ssa" => SubtitleFormat.Ssa,
            ".ttml" or ".dfxp" => SubtitleFormat.Ttml,
            ".sub" => SubtitleFormat.MicroDvd,
            _ => SubtitleFormat.Srt,
        };
    }

    // ---------------------------------------------------------------- SRT

    private static SubtitleDocument ParseSrt(string text)
    {
        var doc = new SubtitleDocument(SubtitleFormat.Srt);
        string[] lines = text.Split('\n');
        SubtitleCue? current = null;
        var body = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            Match m = SrtTime().Match(line);
            if (m.Success)
            {
                Flush();
                current = new SubtitleCue
                {
                    StartMs = ToMs(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value),
                    EndMs = ToMs(m.Groups[5].Value, m.Groups[6].Value, m.Groups[7].Value, m.Groups[8].Value),
                };
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (line.Trim().Length == 0)
            {
                if (IsSrtBoundary(lines, i + 1))
                {
                    Flush();
                }
                else if (body.Length > 0)
                {
                    body.Append('\n');
                }

                continue;
            }

            // Redni broj sljedećeg titla bez praznog retka ispred njega.
            if (IsIndexLine(line) && i + 1 < lines.Length && SrtTime().IsMatch(lines[i + 1]))
            {
                Flush();
                continue;
            }

            if (body.Length > 0 && body[^1] != '\n')
            {
                body.Append('\n');
            }

            body.Append(line.TrimEnd());
        }

        Flush();
        return doc;

        void Flush()
        {
            if (current != null)
            {
                current.Text = CollapseBlankLines(body.ToString());
                if (current.Text.Length > 0)
                {
                    doc.Cues.Add(current);
                }
            }

            current = null;
            body.Clear();
        }
    }

    private static bool IsSrtBoundary(string[] lines, int index)
    {
        int k = index;
        while (k < lines.Length && lines[k].Trim().Length == 0)
        {
            k++;
        }

        if (k >= lines.Length)
        {
            return true;
        }

        if (SrtTime().IsMatch(lines[k]))
        {
            return true;
        }

        return IsIndexLine(lines[k]) && k + 1 < lines.Length && SrtTime().IsMatch(lines[k + 1]);
    }

    private static bool IsIndexLine(string line)
    {
        string t = line.Trim();
        return t.Length is > 0 and < 8 && t.All(char.IsAsciiDigit);
    }

    // ---------------------------------------------------------------- WebVTT

    private static SubtitleDocument ParseVtt(string text)
    {
        var doc = new SubtitleDocument(SubtitleFormat.WebVtt);
        foreach (string block in BlankLines().Split(text))
        {
            string[] lines = block.Trim('\n').Split('\n');
            if (lines.Length == 0)
            {
                continue;
            }

            string first = lines[0].Trim();
            if (first.StartsWith("NOTE", StringComparison.Ordinal) || first.StartsWith("STYLE", StringComparison.Ordinal) ||
                first.StartsWith("REGION", StringComparison.Ordinal))
            {
                continue;
            }

            int timeIndex = Array.FindIndex(lines, l => l.Contains("-->", StringComparison.Ordinal));
            if (timeIndex < 0)
            {
                continue;
            }

            Match m = VttTime().Match(lines[timeIndex]);
            if (!m.Success)
            {
                continue;
            }

            string cueText = CleanVttText(string.Join("\n", lines.Skip(timeIndex + 1)).Trim());
            if (cueText.Length == 0)
            {
                continue;
            }

            doc.Cues.Add(new SubtitleCue
            {
                StartMs = ToMs(Or0(m.Groups[1].Value), m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value),
                EndMs = ToMs(Or0(m.Groups[5].Value), m.Groups[6].Value, m.Groups[7].Value, m.Groups[8].Value),
                Text = cueText,
            });
        }

        return doc;
    }

    private static string CleanVttText(string text)
    {
        // Zadrži samo <i>, <b>, <u> (njih razumije i SRT); ostale oznake (<v Ime>, <c.boja>, vremena) ukloni.
        string cleaned = VttTag().Replace(text, string.Empty);
        return WebUtility.HtmlDecode(cleaned).Replace(' ', ' ');
    }

    // ---------------------------------------------------------------- ASS / SSA

    private static SubtitleDocument ParseAss(string text, SubtitleFormat format)
    {
        var doc = new SubtitleDocument(format);
        var header = new StringBuilder();
        var trailer = new StringBuilder();
        string[]? eventFormat = null;
        int section = 0; // 0 = prije [Events], 1 = [Events], 2 = poslije

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd();
            string t = line.TrimStart();
            if (t.StartsWith('[') && t.EndsWith(']'))
            {
                if (t.Equals("[Events]", StringComparison.OrdinalIgnoreCase))
                {
                    section = 1;
                    header.Append("[Events]\n");
                    continue;
                }

                if (section == 1)
                {
                    section = 2;
                }
            }

            switch (section)
            {
                case 0:
                    header.Append(line).Append('\n');
                    break;
                case 1:
                    if (t.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
                    {
                        eventFormat = t[7..].Split(',').Select(s => s.Trim()).ToArray();
                    }
                    else if (t.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                    {
                        SubtitleCue? cue = ParseAssDialogue(t[9..], eventFormat ?? DefaultFormatFor(format));
                        if (cue != null)
                        {
                            doc.Cues.Add(cue);
                        }
                    }

                    break;
                default:
                    trailer.Append(line).Append('\n');
                    break;
            }
        }

        doc.AssHeader = header.ToString();
        doc.AssEventFormat = eventFormat ?? DefaultFormatFor(format);
        doc.AssTrailer = trailer.Length > 0 ? trailer.ToString() : null;
        return doc;
    }

    internal static string[] DefaultFormatFor(SubtitleFormat format) =>
        format == SubtitleFormat.Ssa ? DefaultSsaFormat : DefaultAssFormat;

    private static SubtitleCue? ParseAssDialogue(string rest, string[] format)
    {
        int startIndex = IndexOf(format, "Start");
        int endIndex = IndexOf(format, "End");
        int textIndex = IndexOf(format, "Text");
        if (startIndex < 0 || endIndex < 0 || textIndex < 0)
        {
            return null;
        }

        string[] parts = rest.Split(',', format.Length);
        if (parts.Length < format.Length)
        {
            return null;
        }

        for (int i = 0; i < parts.Length; i++)
        {
            if (i != textIndex)
            {
                parts[i] = parts[i].Trim();
            }
        }

        long? start = ParseAssTime(parts[startIndex]);
        long? end = ParseAssTime(parts[endIndex]);
        if (start is null || end is null)
        {
            return null;
        }

        return new SubtitleCue { StartMs = start.Value, EndMs = end.Value, Text = parts[textIndex], AssFields = parts };
    }

    private static long? ParseAssTime(string value)
    {
        Match m = AssTime().Match(value);
        return m.Success ? ToMs(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value) : null;
    }

    // ---------------------------------------------------------------- MicroDVD

    private static SubtitleDocument ParseMicroDvd(string text, double? frameRate)
    {
        var doc = new SubtitleDocument(SubtitleFormat.MicroDvd);
        double fps = frameRate is > 1 and < 300 ? frameRate.Value : DefaultFrameRate;
        bool fpsFromFile = false;

        foreach (string line in text.Split('\n'))
        {
            Match m = MicroDvdLine().Match(line.Trim());
            if (!m.Success)
            {
                continue;
            }

            long startFrame = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            string body = m.Groups[3].Value.Trim();

            // Prvi redak oblika {1}{1}23.976 označava broj sličica u sekundi.
            if (doc.Cues.Count == 0 && !fpsFromFile && startFrame <= 1 &&
                double.TryParse(body.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double f) &&
                f is > 1 and < 300)
            {
                fps = f;
                fpsFromFile = true;
                continue;
            }

            long endFrame = m.Groups[2].Value.Length > 0
                ? long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)
                : startFrame + (long)(fps * 3);

            string cueText = CleanMicroDvd(body);
            if (cueText.Length == 0)
            {
                continue;
            }

            doc.Cues.Add(new SubtitleCue
            {
                StartMs = (long)Math.Round(startFrame * 1000.0 / fps),
                EndMs = (long)Math.Round(endFrame * 1000.0 / fps),
                Text = cueText,
            });
        }

        doc.FrameRate = fps;
        return doc;
    }

    private static string CleanMicroDvd(string body)
    {
        bool italicAll = Regex.IsMatch(body, @"^\{y:[^}]*i[^}]*\}", RegexOptions.IgnoreCase);
        bool boldAll = Regex.IsMatch(body, @"^\{y:[^}]*b[^}]*\}", RegexOptions.IgnoreCase);
        var lines = new List<string>();
        foreach (string part in body.Split('|'))
        {
            bool italic = Regex.IsMatch(part, @"\{y:[^}]*i[^}]*\}", RegexOptions.IgnoreCase);
            string line = MicroDvdCode().Replace(part, string.Empty).Trim();
            if (line.StartsWith('/'))
            {
                italic = true;
                line = line[1..].TrimStart();
            }

            if (line.Length == 0)
            {
                continue;
            }

            lines.Add(italic && !italicAll ? $"<i>{line}</i>" : line);
        }

        string result = string.Join("\n", lines);
        if (result.Length == 0)
        {
            return result;
        }

        if (boldAll)
        {
            result = $"<b>{result}</b>";
        }

        return italicAll ? $"<i>{result}</i>" : result;
    }

    // ---------------------------------------------------------------- SubViewer 2

    private static SubtitleDocument ParseSubViewer(string text)
    {
        var doc = new SubtitleDocument(SubtitleFormat.SubViewer);
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            Match m = SubViewerTime().Match(lines[i].Trim());
            if (!m.Success)
            {
                continue;
            }

            var body = new StringBuilder();
            while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0 && !SubViewerTime().IsMatch(lines[i + 1].Trim()))
            {
                if (body.Length > 0)
                {
                    body.Append('\n');
                }

                body.Append(lines[++i].Trim());
            }

            string cueText = body.ToString().Replace("[br]", "\n", StringComparison.OrdinalIgnoreCase).Trim();
            if (cueText.Length == 0)
            {
                continue;
            }

            doc.Cues.Add(new SubtitleCue
            {
                StartMs = ToMs(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value),
                EndMs = ToMs(m.Groups[5].Value, m.Groups[6].Value, m.Groups[7].Value, m.Groups[8].Value),
                Text = cueText,
            });
        }

        return doc;
    }

    // ---------------------------------------------------------------- TTML / DFXP

    private static SubtitleDocument ParseTtml(string text)
    {
        var doc = new SubtitleDocument(SubtitleFormat.Ttml);
        XDocument xml;
        try
        {
            // Sigurnost: bez DTD-a i vanjskih entiteta (zaštita od XXE i "billion laughs").
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                MaxCharactersInDocument = 50_000_000,
            };
            using var reader = XmlReader.Create(new StringReader(text), settings);
            xml = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return doc;
        }

        XElement? root = xml.Root;
        if (root is null)
        {
            return doc;
        }

        double frameRate = ReadDouble(root, "frameRate") ?? 30;
        string? multiplier = Attr(root, "frameRateMultiplier");
        if (multiplier != null)
        {
            string[] mp = multiplier.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (mp.Length == 2 && double.TryParse(mp[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a) &&
                double.TryParse(mp[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double b) && b > 0)
            {
                frameRate *= a / b;
            }
        }

        double tickRate = ReadDouble(root, "tickRate") ?? 1;

        foreach (XElement p in root.Descendants().Where(e => e.Name.LocalName == "p"))
        {
            string? begin = Attr(p, "begin");
            if (begin is null)
            {
                continue;
            }

            long? start = ParseTtmlTime(begin, frameRate, tickRate);
            long? end = Attr(p, "end") is { } e ? ParseTtmlTime(e, frameRate, tickRate)
                : Attr(p, "dur") is { } d && start is not null && ParseTtmlTime(d, frameRate, tickRate) is { } dur ? start + dur
                : null;
            if (start is null || end is null)
            {
                continue;
            }

            string cueText = TtmlText(p);
            if (cueText.Length == 0)
            {
                continue;
            }

            doc.Cues.Add(new SubtitleCue { StartMs = start.Value, EndMs = end.Value, Text = cueText });
        }

        return doc;
    }

    private static long? ParseTtmlTime(string value, double frameRate, double tickRate)
    {
        value = value.Trim();
        Match clock = TtmlClock().Match(value);
        if (clock.Success)
        {
            double seconds = int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 +
                             int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture) * 60 +
                             int.Parse(clock.Groups[3].Value, CultureInfo.InvariantCulture);
            if (clock.Groups[4].Success)
            {
                seconds += double.Parse("0." + clock.Groups[4].Value, CultureInfo.InvariantCulture);
            }
            else if (clock.Groups[5].Success)
            {
                seconds += double.Parse(clock.Groups[5].Value, CultureInfo.InvariantCulture) / frameRate;
            }

            return (long)Math.Round(seconds * 1000);
        }

        Match offset = TtmlOffset().Match(value);
        if (offset.Success)
        {
            double n = double.Parse(offset.Groups[1].Value, CultureInfo.InvariantCulture);
            double seconds = offset.Groups[2].Value switch
            {
                "h" => n * 3600,
                "m" => n * 60,
                "ms" => n / 1000,
                "f" => n / frameRate,
                "t" => n / tickRate,
                _ => n,
            };
            return (long)Math.Round(seconds * 1000);
        }

        return null;
    }

    private static string TtmlText(XElement element)
    {
        var sb = new StringBuilder();
        AppendTtml(element, sb);
        string collapsed = Regex.Replace(sb.ToString(), "[ \t]+", " ");
        return string.Join("\n", collapsed.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
    }

    private static void AppendTtml(XElement element, StringBuilder sb)
    {
        foreach (XNode node in element.Nodes())
        {
            switch (node)
            {
                case XText t:
                    sb.Append(t.Value.Replace('\n', ' ').Replace('\r', ' '));
                    break;
                case XElement e when e.Name.LocalName == "br":
                    sb.Append('\n');
                    break;
                case XElement e:
                    bool italic = string.Equals(Attr(e, "fontStyle"), "italic", StringComparison.OrdinalIgnoreCase);
                    if (italic)
                    {
                        sb.Append("<i>");
                    }

                    AppendTtml(e, sb);
                    if (italic)
                    {
                        sb.Append("</i>");
                    }

                    break;
            }
        }
    }

    private static string? Attr(XElement e, string localName) =>
        e.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;

    private static double? ReadDouble(XElement e, string localName) =>
        Attr(e, localName) is { } v && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0
            ? d
            : null;

    // ---------------------------------------------------------------- pomoćno

    private static string Normalize(string text) =>
        text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');

    private static bool Has(string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static string FirstLine(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            if (line.Trim().Length > 0)
            {
                return line.Trim();
            }
        }

        return string.Empty;
    }

    private static string Or0(string value) => value.Length == 0 ? "0" : value;

    private static int IndexOf(string[] format, string name) =>
        Array.FindIndex(format, f => f.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string CollapseBlankLines(string text)
    {
        var lines = text.Split('\n');
        return string.Join("\n", lines.Where(l => l.Trim().Length > 0)).Trim();
    }

    /// <summary>Sati, minute, sekunde i decimalni dio ("5" = 500 ms, "50" = 500 ms, "500" = 500 ms).</summary>
    internal static long ToMs(string h, string m, string s, string fraction)
    {
        long ms = 0;
        if (fraction.Length > 0)
        {
            string f = fraction.Length >= 3 ? fraction[..3] : fraction.PadRight(3, '0');
            ms = long.Parse(f, CultureInfo.InvariantCulture);
        }

        return ((long.Parse(h, CultureInfo.InvariantCulture) * 60 + long.Parse(m, CultureInfo.InvariantCulture)) * 60 +
                long.Parse(s, CultureInfo.InvariantCulture)) * 1000 + ms;
    }

    [GeneratedRegex(@"(\d{1,3}):(\d{1,2}):(\d{1,2})(?:[,.:](\d{1,3}))?\s*-->\s*(\d{1,3}):(\d{1,2}):(\d{1,2})(?:[,.:](\d{1,3}))?", RegexOptions.CultureInvariant)]
    private static partial Regex SrtTime();

    [GeneratedRegex(@"(?:(\d{1,3}):)?(\d{1,2}):(\d{1,2})[.,](\d{1,3})\s*-->\s*(?:(\d{1,3}):)?(\d{1,2}):(\d{1,2})[.,](\d{1,3})", RegexOptions.CultureInvariant)]
    private static partial Regex VttTime();

    [GeneratedRegex(@"<(?!/?[ibu]>)[^>\n]*>", RegexOptions.CultureInvariant)]
    private static partial Regex VttTag();

    [GeneratedRegex(@"\n[ \t]*\n", RegexOptions.CultureInvariant)]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"^(\d+):(\d{1,2}):(\d{1,2})[.:](\d{1,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex AssTime();

    [GeneratedRegex(@"^\{(\d+)\}\{(\d*)\}(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex MicroDvdLine();

    [GeneratedRegex(@"\{[a-zA-Z]:[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex MicroDvdCode();

    [GeneratedRegex(@"^(\d{1,2}):(\d{2}):(\d{2})\.(\d{1,3}),(\d{1,2}):(\d{2}):(\d{2})\.(\d{1,3})", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex SubViewerTime();

    [GeneratedRegex(@"^(\d+):(\d{2}):(\d{2})(?:\.(\d+)|:(\d+(?:\.\d+)?))?$", RegexOptions.CultureInvariant)]
    private static partial Regex TtmlClock();

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)(h|ms|m|s|f|t)$", RegexOptions.CultureInvariant)]
    private static partial Regex TtmlOffset();
}
