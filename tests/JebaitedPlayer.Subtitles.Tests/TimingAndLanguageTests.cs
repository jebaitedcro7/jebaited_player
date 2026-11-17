namespace JebaitedPlayer.Subtitles.Tests;

[TestClass]
public class TimingTests
{
    [TestMethod]
    public void Offset_of_minus_2000_ms_moves_every_cue_exactly()
    {
        SubtitleDocument doc = SubtitleParser.Parse("1\n00:00:05,000 --> 00:00:07,250\nA\n\n2\n01:00:00,000 --> 01:00:01,000\nB\n");

        SubtitleDocument shifted = new SubtitleTiming(1.0, -2000).ApplyTo(doc);

        Assert.AreEqual(3000, shifted.Cues[0].StartMs);
        Assert.AreEqual(5250, shifted.Cues[0].EndMs);
        Assert.AreEqual(3_598_000, shifted.Cues[1].StartMs);
        Assert.AreEqual(5000, doc.Cues[0].StartMs, "Original se ne smije mijenjati.");
    }

    [TestMethod]
    public void Two_points_give_speed_and_delay()
    {
        // Replika na 4 s treba biti na 5 s, replika na 24 s treba biti na 26 s.
        SubtitleTiming t = SubtitleTiming.FromTwoPoints(4000, 5000, 24000, 26000);

        Assert.AreEqual(1.05, t.Speed, 1e-9);
        Assert.AreEqual(800, t.DelayMs, 1e-6);
        Assert.AreEqual(5000, t.Apply(4000), 1e-6);
        Assert.AreEqual(26000, t.Apply(24000), 1e-6);
        Assert.AreEqual(4000, t.Invert(5000), 1e-6);
    }

    [TestMethod]
    public void Align_point_keeps_speed()
    {
        SubtitleTiming t = new SubtitleTiming(1.04, 0).AlignPoint(10_000, 12_000);

        Assert.AreEqual(1.04, t.Speed, 1e-12);
        Assert.AreEqual(12_000, t.Apply(10_000), 1e-6);
    }

    [TestMethod]
    public void Pal_preset_stretches_25fps_subtitles_for_23976_video()
    {
        double speed = SubtitleTiming.FrameRatePresets[0].Speed;

        // Titl na 1 h u verziji od 25 fps dolazi na ~1 h 2 min 33 s u verziji od 23,976 fps.
        Assert.AreEqual(3_753_750, new SubtitleTiming(speed, 0).Apply(3_600_000), 1);
    }

    [TestMethod]
    public void Srt_writer_round_trip_keeps_times_and_text()
    {
        const string srt = "1\r\n00:00:01,001 --> 00:00:02,999\r\nČčĆćŽžŠšĐđ\r\n\r\n2\r\n10:59:59,000 --> 11:00:00,000\r\n<i>Kraj</i>\r\n\r\n";

        string written = SubtitleWriter.ToSrt(SubtitleParser.Parse(srt));

        Assert.AreEqual(srt, written);
    }

    [TestMethod]
    [DataRow(0L, "00:00:00,000")]
    [DataRow(3_723_004L, "01:02:03,004")]
    [DataRow(-500L, "00:00:00,000")]
    public void Srt_time_format(long ms, string expected) => Assert.AreEqual(expected, SubtitleWriter.FormatSrtTime(ms));

    [TestMethod]
    [DataRow(1234L, "0:00:01.23")]
    [DataRow(1235L, "0:00:01.24")]
    [DataRow(3_723_000L, "1:02:03.00")]
    public void Ass_time_format(long ms, string expected) => Assert.AreEqual(expected, SubtitleWriter.FormatAssTime(ms));

    [TestMethod]
    public void Time_map_moves_each_part_by_its_own_offset()
    {
        // Kao stvarna epizoda (WEB izdanje, titlovi za TV): +0,6 s do 7:43, od 7:58 (pauza između činova) +3,78 s.
        SubtitleDocument doc = SubtitleParser.Parse(
            "1\n00:01:00,000 --> 00:01:02,000\nA\n\n2\n00:07:40,000 --> 00:07:43,000\nB\n\n3\n00:07:58,000 --> 00:08:00,500\nC\n");
        var map = new SubtitleTimeMap([new SubtitleTimeMap.Segment(0, 1, 600), new SubtitleTimeMap.Segment(478_000, 1, 3780)]);

        SubtitleDocument mapped = map.ApplyTo(doc);

        Assert.AreEqual(60_600, mapped.Cues[0].StartMs);
        Assert.AreEqual(460_600, mapped.Cues[1].StartMs);
        Assert.AreEqual(481_780, mapped.Cues[2].StartMs);
        Assert.AreEqual(484_280, mapped.Cues[2].EndMs, "Trajanje replike ostaje isto.");
        Assert.AreEqual(60_000, doc.Cues[0].StartMs, "Original se ne smije mijenjati.");
    }

    [TestMethod]
    public void Gradual_correction_keeps_the_current_line_in_place()
    {
        // Titl na 20:00 je točan (pomak +1,5 s); titlovi kasnije postupno kasne, pa se usporavaju za 0,5 s na 10 min.
        var timing = new SubtitleTiming(1, 1500);
        double anchor = timing.Apply(1_200_000);

        SubtitleTiming slower = timing.WithSpeedAnchored(1 - 0.5 / 600, anchor);

        Assert.AreEqual(anchor, slower.Apply(1_200_000), 1e-6, "Replika na sidru ostaje na mjestu.");
        Assert.AreEqual(anchor + 600_000 - 500, slower.Apply(1_800_000), 1e-6, "10 minuta kasnije: 0,5 s ranije.");
        Assert.AreEqual(-0.5, slower.DriftSecondsPerTenMinutes, 1e-9);
    }

    [TestMethod]
    public void One_anchor_gives_offset_two_consistent_give_pal_speed()
    {
        List<(long, long)> cues = [];

        AnchorSolution one = SyncAnchors.Solve([new SyncAnchor(60_000, 62_500)], cues);
        Assert.AreEqual(2500, one.Timing.DelayMs, 1e-6);
        Assert.IsNull(one.Map);

        // Titlovi za 25 fps: dvije točke daju točno omjer 25 → 23,976 (zaokružen na poznatu vrijednost).
        double pal = SubtitleTiming.FrameRatePresets[0].Speed;
        AnchorSolution two = SyncAnchors.Solve([new SyncAnchor(300_000, 300_000 * pal + 800), new SyncAnchor(2_400_000, 2_400_000 * pal + 750)], cues);
        Assert.AreEqual(pal, two.Timing.Speed, 1e-12);
        Assert.IsNull(two.Map);
        StringAssert.Contains(two.Description, "25 fps");
    }

    [TestMethod]
    public void Anchors_with_jumps_give_parts_split_at_the_longest_pause()
    {
        // Kao stvarna epizoda (WEB izdanje, titlovi za TV): +0,7 s, pa +3,8 s (nakon pauze između činova u 7:50), pa +6,3 s (nakon pauze u 16:20).
        // Replika svake 4 s (2 s govora, 2 s pauze), osim dvije pauze između činova: 15 s prije 7:58 i 49 s prije 16:20.
        var cues = new List<(long, long)>();
        for (long t = 300_000; t <= 1_300_000; t += 4000)
        {
            if (t is > 463_000 and < 478_000 || t is > 931_000 and < 980_000)
            {
                continue;
            }

            cues.Add((t, t + 2000));
        }

        cues.Add((478_000, 480_000));
        cues.Add((980_000, 982_000));
        AnchorSolution s = SyncAnchors.Solve(
        [
            new SyncAnchor(300_000, 300_700), new SyncAnchor(900_000, 903_800), new SyncAnchor(1_300_000, 1_306_300),
        ], cues);

        Assert.IsNotNull(s.Map);
        Assert.AreEqual(3, s.Map.Segments.Count);
        Assert.AreEqual(478_000, s.Map.Segments[1].FromMs, "Granica je u najduljoj pauzi između prve i druge točke.");
        Assert.AreEqual(980_000, s.Map.Segments[2].FromMs);
        Assert.AreEqual(1_306_300, s.Map.Apply(1_300_000), 1e-6);
        Assert.IsTrue(s.Timing.IsIdentity);
    }

    [TestMethod]
    public void Time_map_round_trips_through_text_and_rejects_garbage()
    {
        var map = new SubtitleTimeMap([new SubtitleTimeMap.Segment(463_000.5, 1.0427083333333333, -420.25), new SubtitleTimeMap.Segment(0, 1, 0)]);

        SubtitleTimeMap? again = SubtitleTimeMap.Parse(map.Serialize());

        Assert.IsNotNull(again);
        CollectionAssert.AreEqual(map.Segments.ToArray(), again.Segments.ToArray());
        Assert.AreEqual(0, again.Segments[0].FromMs, "Dijelovi su poredani po vremenu.");
        Assert.IsNull(SubtitleTimeMap.Parse("0:abc:1"));
        Assert.IsNull(SubtitleTimeMap.Parse("0:5:0"), "Nerealna brzina se odbacuje.");
        Assert.IsNull(SubtitleTimeMap.Parse(null));
    }
}

[TestClass]
public class LanguageTests
{
    private const string Croatian =
        "Gdje si bio cijeli tjedan? Tko ti je to rekao? Ne znam što da radim, uvijek je isto. " +
        "Kupio sam kruh i mlijeko za obitelj. Točno u podne idemo na vlak. Nitko nije došao, a netko je ostavio poruku. " +
        "Volim te, ali moram ići. Što ćeš sada? Lijepo je vrijeme danas. Sretan ti rođendan! Da li je to istina? Ne, nije.";

    private const string Serbian =
        "Gde si bio celu nedelju? Ko ti je to rekao? Ne znam šta da radim, uvek je isto. " +
        "Kupio sam hleb i mleko za porodicu. Tačno u podne idemo na voz. Niko nije došao, a neko je ostavio poruku. " +
        "Volim te, ali moram da idem. Šta ćeš sada? Lepo je vreme danas. Srećan ti rođendan! Da li je to istina? Ne, nije.";

    private const string Bosnian =
        "Gdje si bio cijelu sedmicu? Ko ti je to rekao? Ne znam šta da radim, uvijek je isto. " +
        "Kupio sam hljeb i mlijeko za porodicu. Tačno u podne idemo na voz. Niko nije došao, a neko je ostavio poruku. " +
        "Volim te, ali moram ići. Šta ćeš sada? Lijepo je vrijeme danas. Hiljadu puta hvala! Da li je to istina? Ne, nije.";

    private const string English =
        "Where have you been all week? Who told you that? I don't know what to do, it is always the same. " +
        "I bought bread and milk for the family. We are taking the train at noon. Nobody came, but somebody left a message. " +
        "I love you, but I have to go. What will you do now? The weather is nice today.";

    private const string SerbianCyrillic =
        "Где си био целу недељу? Ко ти је то рекао? Не знам шта да радим, увек је исто. Купио сам хлеб и млеко за породицу. " +
        "Тачно у подне идемо на воз. Нико није дошао, а неко је оставио поруку.";

    [TestMethod]
    [DataRow(Croatian, "hr")]
    [DataRow(Serbian, "sr")]
    [DataRow(Bosnian, "bs")]
    [DataRow(English, "en")]
    [DataRow(SerbianCyrillic, "sr")]
    public void Language_is_detected_from_text(string text, string expected) =>
        Assert.AreEqual(expected, SubtitleLanguage.DetectFromText(text));

    [TestMethod]
    [DataRow(@"C:\f\Film.2023.1080p.hr.srt", @"C:\f\Film.2023.1080p.mkv", "hr")]
    [DataRow(@"C:\f\Film.2023.1080p.Croatian.srt", @"C:\f\Film.2023.1080p.mkv", "hr")]
    [DataRow(@"C:\f\Film.srpski.srt", @"C:\f\Film.mkv", "sr")]
    [DataRow(@"C:\f\Subs\2_English.srt", @"C:\f\Film.mkv", "en")]
    [DataRow(@"C:\f\Film.hr.sync.srt", @"C:\f\Film.mkv", "hr")]
    [DataRow(@"C:\f\Film.srt", @"C:\f\Film.mkv", null)]
    public void Language_is_read_from_file_name(string subtitle, string video, string? expected) =>
        Assert.AreEqual(expected, SubtitleLanguage.FromFileName(subtitle, video));

    [TestMethod]
    public void Mpv_slang_follows_priority_and_adds_serbo_croatian_codes()
    {
        Assert.AreEqual("hr,hrv,scr,sr,srp,scc,bs,bos,sh,hbs,en,eng",
            SubtitleLanguage.BuildMpvSlang(["hr", "sr", "bs", "en"]));
    }

    [TestMethod]
    [DataRow("hrv", "hr")]
    [DataRow("scr", "hr")]
    [DataRow("eng", "en")]
    [DataRow("en-US", "en")]
    [DataRow("chi", "chi")]
    [DataRow(null, null)]
    public void Track_language_codes_are_normalized(string? code, string? expected) =>
        Assert.AreEqual(expected, SubtitleLanguage.Normalize(code));
}

[TestClass]
public class FileFinderTests
{
    [TestMethod]
    public void Finds_same_name_subtitles_and_subs_folder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "jp-finder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Subs"));
        try
        {
            string video = Path.Combine(dir, "Film.2023.1080p.mkv");
            foreach (string f in new[]
                     {
                         "Film.2023.1080p.mkv", "Film.2023.1080p.hr.srt", "Film.2023.1080p.srt", "Film.2023.1080p.idx",
                         "Film.2023.1080p.sub", "Drugi.film.srt", "Film.2023.1080p.txt", Path.Combine("Subs", "2_English.srt"),
                     })
            {
                File.WriteAllText(Path.Combine(dir, f), "x");
            }

            var found = SubtitleFileFinder.FindForVideo(video).Select(p => Path.GetRelativePath(dir, p)).ToList();

            CollectionAssert.AreEquivalent(
                new[] { "Film.2023.1080p.hr.srt", "Film.2023.1080p.srt", "Film.2023.1080p.idx", Path.Combine("Subs", "2_English.srt") },
                found);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
