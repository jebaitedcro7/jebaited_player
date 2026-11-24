using System.Globalization;
using System.Xml;

namespace JebaitedPlayer.Iptv;

/// <summary>Streaming parser XMLTV-a (ne učitava cijeli dokument u memoriju). Filtrira po kanalima i vremenskom prozoru.</summary>
public static class XmltvParser
{
    /// <param name="channelFilter">Samo ovi EPG ID-jevi (null = svi).</param>
    /// <param name="from">Programi koji završavaju prije ovoga se odbacuju.</param>
    /// <param name="to">Programi koji počinju nakon ovoga se odbacuju.</param>
    public static List<EpgProgram> Parse(Stream input, ISet<string>? channelFilter, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var result = new List<EpgProgram>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreWhitespace = true };
        using XmlReader r = XmlReader.Create(input, settings);
        bool hasNode = r.Read();
        while (hasNode)
        {
            ct.ThrowIfCancellationRequested();
            if (r.NodeType != XmlNodeType.Element || r.Name != "programme")
            {
                hasNode = r.Read();
                continue;
            }

            string? ch = r.GetAttribute("channel");
            DateTimeOffset? start = ParseTime(r.GetAttribute("start"));
            DateTimeOffset? stop = ParseTime(r.GetAttribute("stop"));
            string? title = null, desc = null;
            if (r.IsEmptyElement)
            {
                hasNode = r.Read();
            }
            else
            {
                int depth = r.Depth;
                hasNode = r.Read();
                while (hasNode && r.Depth > depth)
                {
                    if (r.NodeType == XmlNodeType.Element && r.Name is "title" or "desc")
                    {
                        bool isTitle = r.Name == "title";
                        string text = r.ReadElementContentAsString();
                        if (isTitle) { title ??= text; } else { desc ??= text; }
                        hasNode = !r.EOF;
                    }
                    else
                    {
                        hasNode = r.Read();
                    }
                }
            }

            if (ch is null || start is null || stop is null || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            if ((channelFilter is not null && !channelFilter.Contains(ch)) || stop < from || start > to)
            {
                continue;
            }

            result.Add(new EpgProgram(ch, start.Value, stop.Value, title.Trim(), string.IsNullOrWhiteSpace(desc) ? null : desc.Trim()));
        }

        return result;
    }

    /// <summary>"20261122183000 +0100" ili "20261122183000" (UTC).</summary>
    public static DateTimeOffset? ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        string[] parts = s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string stamp = parts[0];
        if (stamp.Length < 12 || !stamp.All(char.IsDigit))
        {
            return null;
        }

        stamp = stamp.PadRight(14, '0')[..14];
        if (!DateTime.TryParseExact(stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
        {
            return null;
        }

        TimeSpan offset = TimeSpan.Zero;
        if (parts.Length > 1 && parts[1].Length >= 5 && parts[1][0] is '+' or '-'
            && int.TryParse(parts[1][1..3], out int h) && int.TryParse(parts[1][3..5], out int m))
        {
            offset = new TimeSpan(h, m, 0);
            if (parts[1][0] == '-')
            {
                offset = -offset;
            }
        }

        return new DateTimeOffset(dt, offset);
    }
}
