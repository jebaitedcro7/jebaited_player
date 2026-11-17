using System.Text;

namespace JebaitedPlayer.Subtitles.Tests;

[TestClass]
public class EncodingTests
{
    private const string Croatian =
        "1\r\n00:00:01,000 --> 00:00:03,000\r\nČovječe, što ćeš raditi sutra?\r\nĐurđa je žedna, šećer je skuplji.\r\n\r\n" +
        "2\r\n00:00:04,000 --> 00:00:06,000\r\nŽELIM ČAŠU VODE, ĆAO ĐURO! Šuti.\r\n\r\n" +
        "3\r\n00:00:07,000 --> 00:00:09,000\r\nNaći ćemo se kod žute kuće, oko šest.\r\n";

    private const string SerbianCyrillic =
        "1\r\n00:00:01,000 --> 00:00:03,000\r\nЗдраво, како си? Шта ћеш да радиш сутра?\r\n\r\n" +
        "2\r\n00:00:04,000 --> 00:00:06,000\r\nЂорђе је жедан, шећер је скупљи.\r\n";

    private const string French =
        "1\r\n00:00:01,000 --> 00:00:03,000\r\nTrès bien, à demain. Où êtes-vous allés?\r\n\r\n" +
        "2\r\n00:00:04,000 --> 00:00:06,000\r\nLe garçon a été là, près de la fenêtre. Voilà!\r\n";

    [TestMethod]
    [DataRow(1250, 1250, DisplayName = "Windows-1250")]
    [DataRow(28592, 28592, DisplayName = "ISO-8859-2")]
    [DataRow(65001, 65001, DisplayName = "UTF-8 bez BOM-a")]
    public void Croatian_text_is_detected_and_decoded_exactly(int codePage, int expectedCodePage)
    {
        byte[] bytes = SubtitleEncoding.Get(codePage).GetBytes(Croatian);

        string text = SubtitleEncoding.Decode(bytes, null, out EncodingDetection detection);

        Assert.AreEqual(Croatian, text);
        Assert.AreEqual(expectedCodePage, detection.Encoding.CodePage);
    }

    [TestMethod]
    public void Utf8_with_bom_is_detected_and_bom_removed()
    {
        byte[] bytes = [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. Encoding.UTF8.GetBytes(Croatian)];

        string text = SubtitleEncoding.Decode(bytes, null, out EncodingDetection detection);

        Assert.AreEqual(Croatian, text);
        Assert.AreEqual(65001, detection.Encoding.CodePage);
        Assert.AreEqual(3, detection.BomLength);
    }

    [TestMethod]
    [DataRow(true, DisplayName = "UTF-16 LE s BOM-om")]
    [DataRow(false, DisplayName = "UTF-16 LE bez BOM-a")]
    public void Utf16_little_endian_is_detected(bool withBom)
    {
        byte[] body = Encoding.Unicode.GetBytes(Croatian);
        byte[] bytes = withBom ? [0xFF, 0xFE, .. body] : body;

        string text = SubtitleEncoding.Decode(bytes, null, out EncodingDetection detection);

        Assert.AreEqual(Croatian, text);
        Assert.AreEqual(1200, detection.Encoding.CodePage);
    }

    [TestMethod]
    public void Utf16_big_endian_with_bom_is_detected()
    {
        byte[] bytes = [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(Croatian)];

        string text = SubtitleEncoding.Decode(bytes, null, out EncodingDetection detection);

        Assert.AreEqual(Croatian, text);
        Assert.AreEqual(1201, detection.Encoding.CodePage);
    }

    [TestMethod]
    [DataRow(1251, DisplayName = "Windows-1251")]
    [DataRow(65001, DisplayName = "UTF-8")]
    public void Serbian_cyrillic_is_detected(int codePage)
    {
        byte[] bytes = SubtitleEncoding.Get(codePage).GetBytes(SerbianCyrillic);

        string text = SubtitleEncoding.Decode(bytes, null, out EncodingDetection detection);

        Assert.AreEqual(SerbianCyrillic, text);
        Assert.AreEqual(codePage, detection.Encoding.CodePage);
    }

    [TestMethod]
    public void French_windows_1252_is_not_mistaken_for_croatian()
    {
        byte[] bytes = SubtitleEncoding.Get(1252).GetBytes(French);

        string text = SubtitleEncoding.Decode(bytes, null, out _);

        Assert.AreEqual(French, text);
    }

    [TestMethod]
    public void Plain_ascii_is_treated_as_utf8()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("1\r\n00:00:01,000 --> 00:00:02,000\r\nHello there!\r\n");

        SubtitleEncoding.Decode(bytes, null, out EncodingDetection detection);

        Assert.AreEqual(65001, detection.Encoding.CodePage);
    }

    [TestMethod]
    public void Croatian_with_only_c_and_dj_letters_still_decodes_correctly()
    {
        // Č, ć, đ imaju iste bajtove u Windows-1250 i ISO-8859-2; tekst mora biti ispravan u oba slučaja.
        const string sample = "1\r\n00:00:01,000 --> 00:00:02,000\r\nČekaj, ćuti, Đuro, čak i ovdje.\r\n";
        byte[] bytes = SubtitleEncoding.Get(28592).GetBytes(sample);

        string text = SubtitleEncoding.Decode(bytes, null, out _);

        Assert.AreEqual(sample, text);
    }

    [TestMethod]
    public void Forced_encoding_overrides_detection()
    {
        byte[] bytes = SubtitleEncoding.Get(1250).GetBytes(Croatian);

        string text = SubtitleEncoding.Decode(bytes, SubtitleEncoding.Get(1252), out EncodingDetection detection);

        Assert.AreEqual(1252, detection.Encoding.CodePage);
        Assert.AreNotEqual(Croatian, text);
        StringAssert.Contains(detection.DisplayName, "ručno");
    }
}
