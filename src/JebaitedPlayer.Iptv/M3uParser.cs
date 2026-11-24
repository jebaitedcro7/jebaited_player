using System.Text.RegularExpressions;

namespace JebaitedPlayer.Iptv;

/// <summary>Parser M3U/M3U8 popisa kanala (#EXTINF s atributima tvg-id, tvg-logo, group-title, tvg-chno).</summary>
public static partial class M3uParser
{
    [GeneratedRegex("([A-Za-z0-9_-]+)=\"([^\"]*)\"")]
    private static partial Regex AttrRegex();

    public static IptvCatalog Parse(string text) => Parse(new StringReader(text));

    public static IptvCatalog Parse(TextReader reader)
    {
        var channels = new List<IptvChannel>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? xmltv = null;
        string? name = null, group = null, logo = null, epg = null;
        string extGroup = "";
        int number = 0;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in AttrRegex().Matches(line))
                {
                    string key = m.Groups[1].Value;
                    if (key.Equals("url-tvg", StringComparison.OrdinalIgnoreCase) || key.Equals("x-tvg-url", StringComparison.OrdinalIgnoreCase))
                    {
                        xmltv = m.Groups[2].Value.Split(',')[0].Trim();
                    }
                }

                continue;
            }

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in AttrRegex().Matches(line))
                {
                    attrs[m.Groups[1].Value] = m.Groups[2].Value;
                }

                int comma = FirstCommaOutsideQuotes(line);
                name = comma >= 0 ? line[(comma + 1)..].Trim() : null;
                if (string.IsNullOrEmpty(name) && attrs.TryGetValue("tvg-name", out string? tn))
                {
                    name = tn;
                }

                group = attrs.GetValueOrDefault("group-title");
                logo = attrs.GetValueOrDefault("tvg-logo");
                epg = attrs.GetValueOrDefault("tvg-id");
                number = int.TryParse(attrs.GetValueOrDefault("tvg-chno"), out int n) ? n : 0;
                continue;
            }

            if (line.StartsWith("#EXTGRP:", StringComparison.OrdinalIgnoreCase))
            {
                extGroup = line[8..].Trim();
                continue;
            }

            if (line[0] == '#')
            {
                continue;
            }

            if (!Uri.TryCreate(line, UriKind.Absolute, out Uri? uri)
                || uri.Scheme is not ("http" or "https" or "rtmp" or "rtsp" or "udp" or "rtp"))
            {
                name = group = logo = epg = null;
                continue;
            }

            string display = string.IsNullOrWhiteSpace(name) ? uri.Host : name;
            string id = !string.IsNullOrWhiteSpace(epg) ? epg : display;
            string unique = id;
            for (int i = 2; !ids.Add(unique); i++)
            {
                unique = id + "#" + i;
            }

            string grp = !string.IsNullOrWhiteSpace(group) ? group : (extGroup.Length > 0 ? extGroup : "—");
            channels.Add(new IptvChannel(unique, display, line, grp,
                string.IsNullOrWhiteSpace(logo) ? null : logo,
                string.IsNullOrWhiteSpace(epg) ? null : epg,
                number, false));
            name = group = logo = epg = null;
            number = 0;
        }

        return new IptvCatalog(channels, xmltv, null);
    }

    private static int FirstCommaOutsideQuotes(string s)
    {
        bool inQuote = false;

        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"')
            {
                inQuote = !inQuote;
            }
            else if (s[i] == ',' && !inQuote)
            {
                return i;
            }
        }

        return -1;
    }
}
