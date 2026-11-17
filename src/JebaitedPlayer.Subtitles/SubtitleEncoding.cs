using System.Text;
using System.Text.Unicode;

namespace JebaitedPlayer.Subtitles;

public sealed record EncodingDetection(Encoding Encoding, string DisplayName, double Confidence, int BomLength);

/// <summary>
/// Prepoznavanje encodinga titlova, prilagođeno hrvatskom, srpskom, bosanskom i engleskom.
/// <para>
/// Redoslijed: BOM → UTF-16 bez BOM-a → ispravan UTF-8 → bodovanje jednobajtnih kodnih stranica
/// (Windows-1250, ISO-8859-2, Windows-1252, Windows-1251). Bodovanje gleda riječi:
/// č ć ž š đ nose bodove, a znakovi koji nastaju krivim dekodiranjem (ą, ľ, è u "trčs", ¹, ¾,
/// kontrolni znakovi, miješanje ćirilice i latinice u istoj riječi) oduzimaju bodove.
/// </para>
/// </summary>
public static class SubtitleEncoding
{
    private const string SouthSlavicLetters = "čćžšđČĆŽŠĐ";
    private const string WesternLetters = "áàâäãåéèêëíìîïóòôöõúùûüçñýÿßÁÀÂÄÃÅÉÈÊËÍÌÎÏÓÒÔÖÕÚÙÛÜÇÑÝ";
    private const string TypographicPunctuation = "’‘“”„–—…•«»°€";
    private const string ImplausibleLatin = "ąęłńśźżĄĘŁŃŚŹŻľĺŕřůűőťďňěĽĹŔŘŮŰŐŤĎŇĚășțşţĂȘȚŞŢæðþÆÐÞøØœŒ";

    private static readonly int[] SingleByteCandidates = [1250, 28592, 1252, 1251];

    static SubtitleEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Encodinzi koje korisnik može ručno odabrati.</summary>
    /// <remarks>Naziv se čita svaki put, pa prati jezik sučelja.</remarks>
    public static IReadOnlyList<(int CodePage, string Name)> ManualChoices =>
    [
        (65001, "UTF-8"),
        (1250, Loc.T("encoding.cp1250")),
        (28592, "ISO-8859-2 (Latin-2)"),
        (1251, Loc.T("encoding.cp1251")),
        (1252, Loc.T("encoding.cp1252")),
        (1200, "UTF-16 LE"),
        (1201, "UTF-16 BE"),
    ];

    public static Encoding Get(int codePage) => codePage switch
    {
        65001 => new UTF8Encoding(false),
        1200 => new UnicodeEncoding(bigEndian: false, byteOrderMark: false),
        1201 => new UnicodeEncoding(bigEndian: true, byteOrderMark: false),
        _ => Encoding.GetEncoding(codePage),
    };

    public static string NameOf(int codePage)
    {
        foreach (var (cp, name) in ManualChoices)
        {
            if (cp == codePage)
            {
                return name;
            }
        }

        return $"CP{codePage}";
    }

    public static EncodingDetection Detect(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return new EncodingDetection(Get(65001), "UTF-8 (BOM)", 1, 3);
        }

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            return new EncodingDetection(Get(1200), "UTF-16 LE (BOM)", 1, 2);
        }

        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            return new EncodingDetection(Get(1201), "UTF-16 BE (BOM)", 1, 2);
        }

        EncodingDetection? utf16 = DetectUtf16WithoutBom(data);
        if (utf16 != null)
        {
            return utf16;
        }

        if (Array.IndexOf(data, (byte)0) < 0 && !HasHighBytes(data))
        {
            return new EncodingDetection(Get(65001), Loc.T("encoding.asciiOnly"), 1, 0);
        }

        if (Utf8.IsValid(data))
        {
            return new EncodingDetection(Get(65001), "UTF-8", 0.99, 0);
        }

        int bestCp = SingleByteCandidates[0];
        double best = double.NegativeInfinity, second = double.NegativeInfinity;
        foreach (int cp in SingleByteCandidates)
        {
            double score = Score(Get(cp).GetString(data));
            if (score > best)
            {
                second = best;
                best = score;
                bestCp = cp;
            }
            else if (score > second)
            {
                second = score;
            }
        }

        double margin = best - second;
        double confidence = best <= 0 ? 0.3 : Math.Clamp(0.5 + margin / (Math.Abs(best) + 10.0), 0.5, 0.98);
        return new EncodingDetection(Get(bestCp), NameOf(bestCp), confidence, 0);
    }

    /// <summary>Dekodira titlove; ako je <paramref name="forced"/> zadan, koristi njega umjesto prepoznavanja.</summary>
    public static string Decode(byte[] data, Encoding? forced, out EncodingDetection detection)
    {
        if (forced != null)
        {
            detection = new EncodingDetection(forced, Loc.T("encoding.manual", NameOf(forced.CodePage)), 1, BomLength(data, forced.CodePage));
        }
        else
        {
            detection = Detect(data);
        }

        string text = detection.Encoding.GetString(data, detection.BomLength, data.Length - detection.BomLength);
        return text.TrimStart('﻿');
    }

    internal static double Score(string text)
    {
        double score = 0;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsLetter(c))
            {
                bool cyrillic = false, extended = false, ascii = false;
                double wordScore = 0;
                while (i < text.Length && char.IsLetter(text[i]))
                {
                    char w = text[i];
                    if (w < 0x80)
                    {
                        ascii = true;
                    }
                    else if (w is >= 'Ѐ' and <= 'ӿ')
                    {
                        cyrillic = true;
                    }
                    else
                    {
                        extended = true;
                        if (SouthSlavicLetters.Contains(w))
                        {
                            wordScore += 3;
                        }
                        else if (WesternLetters.Contains(w))
                        {
                            wordScore += 1;
                        }
                        else
                        {
                            wordScore -= ImplausibleLatin.Contains(w) ? 2 : 3;
                        }
                    }

                    i++;
                }

                if (cyrillic)
                {
                    score += ascii || extended ? -4 : 2;
                }
                else if (extended)
                {
                    score += wordScore;
                }

                continue;
            }

            if (c >= 0x80)
            {
                if (c is >= '\u0080' and <= '\u009F' or '�')
                {
                    score -= 4;
                }
                else if (TypographicPunctuation.Contains(c))
                {
                    score += 0.5;
                }
                else if (!char.IsWhiteSpace(c))
                {
                    score -= 2;
                }
            }
            else if (c < 0x20 && c is not ('\r' or '\n' or '\t'))
            {
                score -= 1;
            }

            i++;
        }

        return score;
    }

    private static EncodingDetection? DetectUtf16WithoutBom(byte[] data)
    {
        int n = Math.Min(data.Length, 8192) & ~1;
        if (n < 64)
        {
            return null;
        }

        int evenZero = 0, oddZero = 0;
        for (int i = 0; i < n; i += 2)
        {
            if (data[i] == 0)
            {
                evenZero++;
            }

            if (data[i + 1] == 0)
            {
                oddZero++;
            }
        }

        int pairs = n / 2;
        if (oddZero > pairs * 0.3 && evenZero < pairs * 0.05)
        {
            return new EncodingDetection(Get(1200), "UTF-16 LE", 0.9, 0);
        }

        if (evenZero > pairs * 0.3 && oddZero < pairs * 0.05)
        {
            return new EncodingDetection(Get(1201), "UTF-16 BE", 0.9, 0);
        }

        return null;
    }

    private static bool HasHighBytes(byte[] data)
    {
        foreach (byte b in data)
        {
            if (b >= 0x80)
            {
                return true;
            }
        }

        return false;
    }

    private static int BomLength(byte[] data, int codePage) => codePage switch
    {
        65001 when data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF => 3,
        1200 when data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE => 2,
        1201 when data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF => 2,
        _ => 0,
    };
}
