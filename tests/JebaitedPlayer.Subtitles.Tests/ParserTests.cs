namespace JebaitedPlayer.Subtitles.Tests;

[TestClass]
public class ParserTests
{
    [TestMethod]
    public void Srt_standard_file_is_parsed()
    {
        const string srt = "1\r\n00:00:01,000 --> 00:00:03,500\r\nPrvi redak\r\nDrugi redak\r\n\r\n2\r\n00:01:02,250 --> 00:01:04,000\r\n<i>Kurziv</i>\r\n";

        SubtitleDocument doc = SubtitleParser.Parse(srt);

        Assert.AreEqual(SubtitleFormat.Srt, doc.Format);
        Assert.AreEqual(2, doc.Cues.Count);
        Assert.AreEqual(1000, doc.Cues[0].StartMs);
        Assert.AreEqual(3500, doc.Cues[0].EndMs);
        Assert.AreEqual("Prvi redak\nDrugi redak", doc.Cues[0].Text);
        Assert.AreEqual(62250, doc.Cues[1].StartMs);
        Assert.AreEqual("<i>Kurziv</i>", doc.Cues[1].Text);
    }

    [TestMethod]
    public void Srt_tolerates_common_mistakes()
    {
        // LF, točka umjesto zareza, jednoznamenkasti sati, nedostaje prazan redak, višak praznih redaka, bez završnog retka.
        const string srt = "\n\n1\n0:00:01.5 --> 0:00:02.75\nA\n2\n00:00:03,000 --> 00:00:04,000\nB\n\n\n\n7\n00:00:05,000-->00:00:06,000\nC";

        SubtitleDocument doc = SubtitleParser.Parse(srt);

        Assert.AreEqual(3, doc.Cues.Count);
        Assert.AreEqual(1500, doc.Cues[0].StartMs);
        Assert.AreEqual(2750, doc.Cues[0].EndMs);
        Assert.AreEqual("A", doc.Cues[0].Text);
        Assert.AreEqual("B", doc.Cues[1].Text);
        Assert.AreEqual(5000, doc.Cues[2].StartMs);
        Assert.AreEqual("C", doc.Cues[2].Text);
    }

    [TestMethod]
    public void Srt_number_inside_text_is_kept()
    {
        const string srt = "1\n00:00:01,000 --> 00:00:02,000\nImam\n42\ngodine\n\n2\n00:00:03,000 --> 00:00:04,000\nDa\n";

        SubtitleDocument doc = SubtitleParser.Parse(srt);

        Assert.AreEqual(2, doc.Cues.Count);
        Assert.AreEqual("Imam\n42\ngodine", doc.Cues[0].Text);
    }

    [TestMethod]
    public void WebVtt_is_parsed_and_tags_cleaned()
    {
        const string vtt = "WEBVTT - naslov\nKind: captions\n\nNOTE ovo je komentar\n\nid-1\n00:01.000 --> 00:04.000 align:start position:10%\n<v Marko>Bok &amp; doviđenja</v>\n<i>kurziv</i> <c.yellow>žuto</c>\n\n01:00:00.000 --> 01:00:02.500\nKasno\n";

        SubtitleDocument doc = SubtitleParser.Parse(vtt);

        Assert.AreEqual(SubtitleFormat.WebVtt, doc.Format);
        Assert.AreEqual(2, doc.Cues.Count);
        Assert.AreEqual(1000, doc.Cues[0].StartMs);
        Assert.AreEqual(4000, doc.Cues[0].EndMs);
        Assert.AreEqual("Bok & doviđenja\n<i>kurziv</i> žuto", doc.Cues[0].Text);
        Assert.AreEqual(3_600_000, doc.Cues[1].StartMs);
    }

    [TestMethod]
    public void Ass_dialogue_with_commas_and_custom_format_is_parsed()
    {
        const string ass = "[Script Info]\nTitle: Test\nScriptType: v4.00+\n\n[V4+ Styles]\nFormat: Name, Fontname\nStyle: Glavni,Arial\n\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nComment: 0,0:00:00.00,0:00:01.00,Glavni,,0,0,0,,komentar\nDialogue: 0,0:00:01.50,0:00:03.25,Glavni,Ana,0,0,0,,Bok, kako si?\\NDobro, hvala.\n";

        SubtitleDocument doc = SubtitleParser.Parse(ass);

        Assert.AreEqual(SubtitleFormat.Ass, doc.Format);
        Assert.AreEqual(1, doc.Cues.Count);
        Assert.AreEqual(1500, doc.Cues[0].StartMs);
        Assert.AreEqual(3250, doc.Cues[0].EndMs);
        Assert.AreEqual("Bok, kako si?\\NDobro, hvala.", doc.Cues[0].Text);
        StringAssert.Contains(doc.AssHeader, "Style: Glavni,Arial");
    }

    [TestMethod]
    public void Ass_round_trip_keeps_styles_and_text()
    {
        const string ass = "[Script Info]\nScriptType: v4.00+\n\n[V4+ Styles]\nFormat: Name, Fontname\nStyle: Glavni,Arial\n\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 1,0:00:01.50,0:00:03.25,Glavni,Ana,0,0,10,,{\\i1}Bok{\\i0}, ćao!\n";

        SubtitleDocument doc = SubtitleParser.Parse(ass);
        string written = SubtitleWriter.ToAss(new SubtitleTiming(1, 1000).ApplyTo(doc));
        SubtitleDocument again = SubtitleParser.Parse(written);

        StringAssert.Contains(written, "Style: Glavni,Arial");
        StringAssert.Contains(written, "Dialogue: 1,0:00:02.50,0:00:04.25,Glavni,Ana,0,0,10,,{\\i1}Bok{\\i0}, ćao!");
        Assert.AreEqual(2500, again.Cues[0].StartMs);
    }

    [TestMethod]
    public void MicroDvd_uses_frame_rate_line_from_file()
    {
        const string sub = "{1}{1}25\n{25}{50}Prvi|Drugi redak\n{75}{100}{y:i}Kurziv\n";

        SubtitleDocument doc = SubtitleParser.Parse(sub, "film.sub");

        Assert.AreEqual(SubtitleFormat.MicroDvd, doc.Format);
        Assert.AreEqual(2, doc.Cues.Count);
        Assert.AreEqual(1000, doc.Cues[0].StartMs);
        Assert.AreEqual(2000, doc.Cues[0].EndMs);
        Assert.AreEqual("Prvi\nDrugi redak", doc.Cues[0].Text);
        Assert.AreEqual("<i>Kurziv</i>", doc.Cues[1].Text);
        Assert.AreEqual(25, doc.FrameRate);
    }

    [TestMethod]
    public void MicroDvd_without_frame_rate_line_uses_video_frame_rate()
    {
        SubtitleDocument doc = SubtitleParser.Parse("{240}{480}Tekst\n", "film.sub", 24);

        Assert.AreEqual(10_000, doc.Cues[0].StartMs);
        Assert.AreEqual(20_000, doc.Cues[0].EndMs);
    }

    [TestMethod]
    public void SubViewer_is_parsed()
    {
        const string sub = "[INFORMATION]\n[TITLE]Film\n[END INFORMATION]\n[SUBTITLE]\n00:00:01.00,00:00:02.50\nPrvi[br]Drugi\n\n00:00:03.00,00:00:04.00\nTreći\n";

        SubtitleDocument doc = SubtitleParser.Parse(sub, "film.sub");

        Assert.AreEqual(SubtitleFormat.SubViewer, doc.Format);
        Assert.AreEqual(2, doc.Cues.Count);
        Assert.AreEqual(2500, doc.Cues[0].EndMs);
        Assert.AreEqual("Prvi\nDrugi", doc.Cues[0].Text);
    }

    [TestMethod]
    public void Ttml_clock_offset_and_tick_times_are_parsed()
    {
        const string ttml = """
            <?xml version="1.0" encoding="utf-8"?>
            <tt xmlns="http://www.w3.org/ns/ttml" xmlns:tts="http://www.w3.org/ns/ttml#styling" xmlns:ttp="http://www.w3.org/ns/ttml#parameter" ttp:tickRate="10000000">
              <body><div>
                <p begin="00:00:01.500" end="00:00:03.000">Prvi<br/>redak</p>
                <p begin="4s" dur="1500ms"><span tts:fontStyle="italic">Kurziv</span> tekst</p>
                <p begin="50000000t" end="60000000t">Tikovi</p>
              </div></body>
            </tt>
            """;

        SubtitleDocument doc = SubtitleParser.Parse(ttml, "film.ttml");

        Assert.AreEqual(SubtitleFormat.Ttml, doc.Format);
        Assert.AreEqual(3, doc.Cues.Count);
        Assert.AreEqual(1500, doc.Cues[0].StartMs);
        Assert.AreEqual("Prvi\nredak", doc.Cues[0].Text);
        Assert.AreEqual(4000, doc.Cues[1].StartMs);
        Assert.AreEqual(5500, doc.Cues[1].EndMs);
        Assert.AreEqual("<i>Kurziv</i> tekst", doc.Cues[1].Text);
        Assert.AreEqual(5000, doc.Cues[2].StartMs);
        Assert.AreEqual(6000, doc.Cues[2].EndMs);
    }

    [TestMethod]
    public void Ttml_with_dtd_entities_is_rejected_safely()
    {
        const string xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE tt [<!ENTITY xxe SYSTEM "file:///c:/windows/win.ini">]>
            <tt xmlns="http://www.w3.org/ns/ttml"><body><div><p begin="1s" end="2s">&xxe;</p></div></body></tt>
            """;

        SubtitleDocument doc = SubtitleParser.Parse(xxe, "napad.ttml");

        Assert.AreEqual(0, doc.Cues.Count);
    }

    [TestMethod]
    [DataRow("WEBVTT\n\n00:01.000 --> 00:02.000\nx", SubtitleFormat.WebVtt)]
    [DataRow("[Script Info]\nScriptType: v4.00+\n[V4+ Styles]\n[Events]\n", SubtitleFormat.Ass)]
    [DataRow("[Script Info]\nScriptType: v4.00\n[V4 Styles]\n[Events]\n", SubtitleFormat.Ssa)]
    [DataRow("{10}{20}Bok", SubtitleFormat.MicroDvd)]
    [DataRow("1\n00:00:01,000 --> 00:00:02,000\nBok", SubtitleFormat.Srt)]
    public void Format_is_detected_from_content(string content, SubtitleFormat expected)
    {
        Assert.AreEqual(expected, SubtitleParser.DetectFormat(content, "nepoznato.txt"));
    }
}
