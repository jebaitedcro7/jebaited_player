using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace JebaitedPlayer.Views;

/// <summary>
/// Zastave država nacrtane vektorski (bez preuzimanja slika, bez mreže). Pojednostavljene su: bez grbova i sitnih detalja.
/// Naziv kategorije (npr. „Hrvatska”, „Germany Sport”) prepoznaje se po riječima; nepoznati nazivi nemaju zastavu.
/// </summary>
internal static partial class FlagFactory
{
    private const double W = 30, H = 20;
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    // Šifra države → riječi u nazivu kategorije ('*' na kraju = početak riječi).
    private static readonly (string Code, string[] Words)[] Names =
    [
        ("ba", ["bosna", "bosnia*", "bih", "hercegovina"]),
        ("hr", ["hrvatska", "hrvat*", "croatia*", "croat*", "kroatien"]),
        ("rs", ["srbija", "serbia*", "srb*", "serbien"]),
        ("si", ["slovenija", "slovenia*", "sloven*"]),
        ("mk", ["makedonija", "macedonia*", "makedon*", "macedon*"]),
        ("me", ["crna", "montenegro", "cg"]),
        ("al", ["albania*", "albanija", "alban*", "shqip*"]),
        ("at", ["austria*", "austrija", "osterreich", "osterreich"]),
        ("de", ["germany", "german*", "deutsch*", "njemacka", "njemacka", "njemac*"]),
        ("fr", ["france", "francuska", "franc*"]),
        ("it", ["italia", "italy", "italija", "ital*"]),
        ("ch", ["switzerland", "schweiz", "swiss", "svicarska", "svicarska", "svajcarska"]),
        ("se", ["sweden", "svedska", "svedska", "sverige"]),
        ("dk", ["denmark", "danska", "danmark"]),
        ("no", ["norway", "norveska", "norveska", "norge", "norwegen"]),
        ("fi", ["finland", "finska", "suomi"]),
        ("nl", ["nederlands", "nederland*", "netherlands", "holland*", "nizozemska"]),
        ("be", ["belgium", "belgija", "belgie", "belg*"]),
        ("lu", ["luxembourg", "luxemburg", "luksemburg"]),
        ("gb", ["uk", "britain", "british", "england", "engleska", "unitedkingdom", "gb"]),
        ("us", ["usa", "us", "america", "american*", "sad"]),
        ("tr", ["turkey", "turkay", "turska", "turkiye", "turkiye", "turk*", "turski"]),
        ("hu", ["hungarian", "hungary", "madjarska", "madjarska", "madarska", "magyar*"]),
        ("pl", ["poland", "polska", "poljska", "polish"]),
        ("cz", ["czech", "ceska", "ceska", "ceska"]),
        ("sk", ["slovakia", "slovacka", "slovacka", "slovensko"]),
        ("bg", ["bulgaria", "bulgarska", "bugarska", "bulg*"]),
        ("ro", ["romania", "rumunjska", "romanian", "romania"]),
        ("es", ["spain", "spanjolska", "spanjolska", "espana", "spanish"]),
        ("pt", ["portugal", "portugalska", "portug*"]),
        ("gr", ["greece", "grcka", "grcka", "greek", "ellada"]),
        ("ru", ["russia*", "rusija", "russian"]),
        ("ua", ["ukraine", "ukrajina", "ukrain*"]),
        ("ie", ["ireland", "irska", "irish"]),
        ("ca", ["canada", "kanada"]),
        ("jp", ["japan"]),
        ("cn", ["china", "kina"]),
    ];

    private static readonly (string Emoji, string[] Words)[] Topics =
    [
        ("⚽", ["sport", "sports", "sportski"]),
        ("\U0001F3AC", ["film", "films", "movie", "movies", "filmovi", "cinema", "kino"]),
        ("\U0001F3B5", ["muzika", "music", "musik", "glazba", "mtv"]),
        ("\U0001F9F8", ["djeci", "djeca", "kids", "kinder", "child*", "cartoon*", "crtani"]),
        ("\U0001F4DA", ["dokumentarni", "documentary", "documentaries", "dokumentari", "docu*"]),
        ("\U0001F4F0", ["news", "vijesti", "vesti", "nachrichten", "info"]),
        ("\U0001F30D", ["world", "international", "svijet", "svet"]),
    ];

    [GeneratedRegex(@"[^\p{L}]+")]
    private static partial Regex Splitter();

    private static IEnumerable<string> Tokens(string name) =>
        Splitter().Split(Normalize(name)).Where(t => t.Length > 0);

    /// <summary>Mala slova bez dijakritika (c, s, dj...), da se nazivi podudaraju neovisno o pisanju.</summary>
    private static string Normalize(string name)
    {
        string d = name.ToLowerInvariant().Replace("\u0111", "dj").Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }

    private static bool Matches(string token, string[] words) =>
        words.Any(w => w.EndsWith('*') ? token.StartsWith(w[..^1], StringComparison.Ordinal) : token == w);

    /// <summary>Šifra države za naziv kategorije ili null.</summary>
    internal static string? CodeFor(string categoryName)
    {
        string[] tokens = Tokens(categoryName).ToArray();
        foreach ((string code, string[] words) in Names)
        {
            if (tokens.Any(t => Matches(t, words)))
            {
                return code;
            }
        }

        return null;
    }

    /// <summary>Emoji teme (sport, film, glazba…) za kategoriju koja nije država, inače null.</summary>
    internal static string? EmojiFor(string categoryName)
    {
        string[] tokens = Tokens(categoryName).ToArray();
        foreach ((string emoji, string[] words) in Topics)
        {
            if (tokens.Any(t => Matches(t, words)))
            {
                return emoji;
            }
        }

        return null;
    }

    /// <summary>Zastava za naziv kategorije (zamrznuta slika, dijeli se) ili null.</summary>
    internal static ImageSource? ForName(string categoryName)
    {
        string? code = CodeFor(categoryName);
        return code is null ? null : ForCode(code);
    }

    internal static ImageSource? ForCode(string code)
    {
        if (Cache.TryGetValue(code, out ImageSource? cached))
        {
            return cached;
        }

        DrawingGroup? g = Build(code);
        ImageSource? image = null;
        if (g is not null)
        {
            g.ClipGeometry = new RectangleGeometry(new Rect(0, 0, W, H));
            g.Freeze();
            var di = new DrawingImage(g);
            di.Freeze();
            image = di;
        }

        Cache[code] = image;
        return image;
    }

    // --- Crtanje -------------------------------------------------------------------------------------------------

    private static Brush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static Drawing R(string hex, double x, double y, double w, double h) =>
        new GeometryDrawing(B(hex), null, new RectangleGeometry(new Rect(x, y, w, h)));

    private static Drawing C(string hex, double cx, double cy, double r) =>
        new GeometryDrawing(B(hex), null, new EllipseGeometry(new Point(cx, cy), r, r));

    private static Drawing P(string hex, params double[] xy)
    {
        var fig = new PathFigure { StartPoint = new Point(xy[0], xy[1]), IsClosed = true };
        for (int i = 2; i < xy.Length; i += 2)
        {
            fig.Segments.Add(new LineSegment(new Point(xy[i], xy[i + 1]), true));
        }

        return new GeometryDrawing(B(hex), null, new PathGeometry([fig]));
    }

    private static Drawing L(string hex, double x1, double y1, double x2, double y2, double t)
    {
        var pen = new Pen(B(hex), t);
        pen.Freeze();
        return new GeometryDrawing(null, pen, new LineGeometry(new Point(x1, y1), new Point(x2, y2)));
    }

    private static Drawing Star(string hex, double cx, double cy, double outer)
    {
        var pts = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5;
            double r = i % 2 == 0 ? outer : outer * 0.4;
            pts.Add(cx + r * Math.Cos(a));
            pts.Add(cy + r * Math.Sin(a));
        }

        return P(hex, pts.ToArray());
    }

    private static DrawingGroup G(params Drawing[] parts)
    {
        var g = new DrawingGroup();
        foreach (Drawing d in parts)
        {
            g.Children.Add(d);
        }

        return g;
    }

    private static Drawing[] H3(string a, string b, string c) => [R(a, 0, 0, W, H / 3 + .1), R(b, 0, H / 3, W, H / 3 + .1), R(c, 0, 2 * H / 3, W, H / 3 + .1)];

    private static Drawing[] V3(string a, string b, string c) => [R(a, 0, 0, W / 3 + .1, H), R(b, W / 3, 0, W / 3 + .1, H), R(c, 2 * W / 3, 0, W / 3 + .1, H)];

    private static DrawingGroup With(Drawing[] baseParts, params Drawing[] extra) => G([.. baseParts, .. extra]);

    private static DrawingGroup? Build(string code) => code switch
    {
        "hr" => With(H3("#FF0000", "#FFFFFF", "#171796"), R("#FFFFFF", 12.5, 5.5, 5, 7), R("#FF0000", 12.5, 5.5, 2.5, 2.33), R("#FF0000", 15, 7.83, 2.5, 2.33), R("#FF0000", 12.5, 10.16, 2.5, 2.34)),
        "rs" => With(H3("#C6363C", "#0C4076", "#FFFFFF"), R("#C6363C", 7, 5.5, 5, 7), R("#EEBB44", 8, 6.2, 3, 1.2), R("#FFFFFF", 8.7, 8, 1.6, 3.2)),
        "si" => With(H3("#FFFFFF", "#005DA4", "#ED1C24"), R("#005DA4", 5, 4, 5.5, 6.5), R("#FFFFFF", 6.6, 5.6, 2.3, 1.2), R("#ED1C24", 6.2, 8, 3.1, 1.2)),
        "ba" => G(R("#002395", 0, 0, W, H), P("#FECB00", 7, 0, 23, 0, 23, 16), C("#FFFFFF", 9, 1.7, .8), C("#FFFFFF", 11.5, 4, .8), C("#FFFFFF", 14, 6.3, .8), C("#FFFFFF", 16.5, 8.6, .8), C("#FFFFFF", 19, 10.9, .8), C("#FFFFFF", 21.5, 13.2, .8)),
        "mk" => G(R("#D20000", 0, 0, W, H), L("#FFE600", 0, 10, W, 10, 2), L("#FFE600", 15, 0, 15, H, 2), L("#FFE600", 0, 0, W, H, 2), L("#FFE600", W, 0, 0, H, 2), C("#D20000", 15, 10, 4.6), C("#FFE600", 15, 10, 3.6)),
        "me" => G(R("#D3AE3B", 0, 0, W, H), R("#C40308", 1.2, 1.2, W - 2.4, H - 2.4), R("#D3AE3B", 12, 6.5, 6, 7), R("#C40308", 13, 7.5, 4, 5)),
        "al" => G(R("#E41E20", 0, 0, W, H), P("#000000", 11, 5, 15, 8, 19, 5, 17, 10, 19, 15, 15, 12, 11, 15, 13, 10)),
        "at" => G(H3("#ED2939", "#FFFFFF", "#ED2939")),
        "de" => G(H3("#000000", "#DD0000", "#FFCE00")),
        "fr" => G(V3("#0055A4", "#FFFFFF", "#EF4135")),
        "it" => G(V3("#009246", "#FFFFFF", "#CE2B37")),
        "ch" => G(R("#DA291C", 0, 0, W, H), R("#FFFFFF", 12.5, 3.5, 5, 13), R("#FFFFFF", 8.5, 7.5, 13, 5)),
        "se" => G(R("#006AA7", 0, 0, W, H), R("#FECC00", 8.5, 0, 4, H), R("#FECC00", 0, 8, W, 4)),
        "dk" => G(R("#C8102E", 0, 0, W, H), R("#FFFFFF", 9, 0, 3.5, H), R("#FFFFFF", 0, 8.25, W, 3.5)),
        "no" => G(R("#BA0C2F", 0, 0, W, H), R("#FFFFFF", 8, 0, 6, H), R("#FFFFFF", 0, 7, W, 6), R("#00205B", 9.5, 0, 3, H), R("#00205B", 0, 8.5, W, 3)),
        "fi" => G(R("#FFFFFF", 0, 0, W, H), R("#003580", 8, 0, 4.5, H), R("#003580", 0, 7.75, W, 4.5)),
        "nl" => G(H3("#AE1C28", "#FFFFFF", "#21468B")),
        "be" => G(V3("#000000", "#FAE042", "#ED2939")),
        "lu" => G(H3("#ED2939", "#FFFFFF", "#00A1DE")),
        "pl" => G(R("#FFFFFF", 0, 0, W, H / 2), R("#DC143C", 0, H / 2, W, H / 2)),
        "cz" => G(R("#FFFFFF", 0, 0, W, H / 2), R("#D7141A", 0, H / 2, W, H / 2), P("#11457E", 0, 0, 15, 10, 0, 20)),
        "sk" => With(H3("#FFFFFF", "#0B4EA2", "#EE1C25"), R("#EE1C25", 7, 4.5, 6.5, 9), R("#FFFFFF", 9.8, 6, 1, 6), R("#FFFFFF", 8.2, 8, 4.2, 1)),
        "hu" => G(H3("#CD2A3E", "#FFFFFF", "#436F4D")),
        "bg" => G(H3("#FFFFFF", "#00966E", "#D62612")),
        "ro" => G(V3("#002B7F", "#FCD116", "#CE1126")),
        "ru" => G(H3("#FFFFFF", "#0039A6", "#D52B1E")),
        "ua" => G(R("#0057B7", 0, 0, W, H / 2), R("#FFD700", 0, H / 2, W, H / 2)),
        "ie" => G(V3("#169B62", "#FFFFFF", "#FF883E")),
        "es" => G(R("#AA151B", 0, 0, W, H), R("#F1BF00", 0, 5, W, 10), R("#AA151B", 7, 7.5, 3, 5)),
        "pt" => G(R("#006600", 0, 0, 12, H), R("#FF0000", 12, 0, 18, H), C("#FFD700", 12, 10, 3.6), C("#FF0000", 12, 10, 2.2), C("#FFFFFF", 12, 10, 1.1)),
        "gr" => G([R("#0D5EAF", 0, 0, W, H), .. new[] { 1, 3, 5, 7 }.Select(i => R("#FFFFFF", 0, i * H / 9, W, H / 9)), R("#0D5EAF", 0, 0, 11.2, 11.1), R("#FFFFFF", 4.5, 0, 2.2, 11.1), R("#FFFFFF", 0, 4.4, 11.2, 2.2)]),
        "gb" => G(R("#012169", 0, 0, W, H), L("#FFFFFF", 0, 0, W, H, 4), L("#FFFFFF", W, 0, 0, H, 4), L("#C8102E", 0, 0, W, H, 1.4), L("#C8102E", W, 0, 0, H, 1.4),
            R("#FFFFFF", 12, 0, 6, H), R("#FFFFFF", 0, 7, W, 6), R("#C8102E", 13, 0, 4, H), R("#C8102E", 0, 8, W, 4)),
        "us" => G([R("#FFFFFF", 0, 0, W, H), .. new[] { 0, 2, 4, 6, 8, 10, 12 }.Select(i => R("#B22234", 0, i * H / 13, W, H / 13)), R("#3C3B6E", 0, 0, 12.5, 10.8),
            .. new[] { (2.5, 2.2), (6.2, 2.2), (9.9, 2.2), (4.3, 5.4), (8.1, 5.4), (2.5, 8.4), (6.2, 8.4), (9.9, 8.4) }.Select(p => C("#FFFFFF", p.Item1, p.Item2, .75))]),
        "tr" => G(R("#E30A17", 0, 0, W, H), C("#FFFFFF", 11, 10, 5.2), C("#E30A17", 12.4, 10, 4.2), Star("#FFFFFF", 17, 10, 2.6)),
        "ca" => G(R("#D52B1E", 0, 0, 7.5, H), R("#FFFFFF", 7.5, 0, 15, H), R("#D52B1E", 22.5, 0, 7.5, H), Star("#D52B1E", 15, 10, 4.5)),
        "jp" => G(R("#FFFFFF", 0, 0, W, H), C("#BC002D", 15, 10, 6)),
        "cn" => G(R("#DE2910", 0, 0, W, H), Star("#FFDE00", 6, 5.2, 3.6)),
        _ => null,
    };
}
