using System.Buffers.Binary;
using System.Text;
using JebaitedPlayer.Library;

namespace JebaitedPlayer.Library.Tests;

/// <summary>2.1.12: omot albuma iz oznaka (ID3v2.2–2.4, FLAC, MP4, Ogg) i iz mape; neispravne datoteke nikad ne bacaju.</summary>
[TestClass]
public sealed class CoverArtTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "jp-cover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, true);

    /// <summary>„Slika”: ispravan potpis PNG-a ili JPEG-a i oznaka po kojoj se prepoznaje.</summary>
    private static byte[] Png(byte marker) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker, 1, 2, 3, 4, 5, 6, 7];

    private static byte[] Jpeg(byte marker) => [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', marker, 0xFF, 0xD9];

    private static byte[] SyncSafe(int v) => [(byte)((v >> 21) & 0x7F), (byte)((v >> 14) & 0x7F), (byte)((v >> 7) & 0x7F), (byte)(v & 0x7F)];

    private static byte[] Be32(int v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        return b;
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] Apic(int type, byte[] image, byte encoding = 0)
    {
        byte[] description = encoding == 1 ? Concat([0xFF, 0xFE], Encoding.Unicode.GetBytes("Omot"), [0, 0]) : Concat(Encoding.ASCII.GetBytes("Omot"), [0]);
        return Concat([encoding], Encoding.ASCII.GetBytes("image/png"), [0], [(byte)type], description, image);
    }

    private static byte[] Id3v23(params (string Id, byte[] Body)[] frames)
    {
        byte[] body = Concat(frames.Select(f => Concat(Encoding.ASCII.GetBytes(f.Id), Be32(f.Body.Length), [0, 0], f.Body)).ToArray());
        return Concat(Encoding.ASCII.GetBytes("ID3"), [3, 0, 0], SyncSafe(body.Length), body);
    }

    private static byte[] FakeMp3Audio => [0xFF, 0xFB, 0x90, 0x64, 0, 0, 0, 0];

    private string Write(string name, byte[] data)
    {
        string path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    [TestMethod]
    public void Id3v23_prefers_the_front_cover()
    {
        byte[] tag = Id3v23(("TIT2", Concat([0], Encoding.ASCII.GetBytes("Naslov"))), ("APIC", Apic(0, Png(1))), ("APIC", Apic(3, Png(2))));
        string mp3 = Write("a.mp3", Concat(tag, FakeMp3Audio));
        CollectionAssert.AreEqual(Png(2), CoverArt.ReadEmbedded(mp3));
    }

    [TestMethod]
    public void Id3v24_with_utf16_description_and_data_length_indicator()
    {
        byte[] apic = Apic(3, Jpeg(7), encoding: 1);
        byte[] frameBody = Concat(SyncSafe(apic.Length), apic); // pokazatelj duljine (zastavica 0x01)
        byte[] frame = Concat(Encoding.ASCII.GetBytes("APIC"), SyncSafe(frameBody.Length), [0, 0x01], frameBody);
        byte[] tag = Concat(Encoding.ASCII.GetBytes("ID3"), [4, 0, 0], SyncSafe(frame.Length), frame);
        string mp3 = Write("b.mp3", Concat(tag, FakeMp3Audio));
        CollectionAssert.AreEqual(Jpeg(7), CoverArt.ReadEmbedded(mp3));
    }

    [TestMethod]
    public void Id3v22_pic_frame()
    {
        byte[] pic = Concat([0], Encoding.ASCII.GetBytes("PNG"), [3], Encoding.ASCII.GetBytes("x"), [0], Png(9));
        byte[] frame = Concat(Encoding.ASCII.GetBytes("PIC"), [(byte)(pic.Length >> 16), (byte)(pic.Length >> 8), (byte)pic.Length], pic);
        byte[] tag = Concat(Encoding.ASCII.GetBytes("ID3"), [2, 0, 0], SyncSafe(frame.Length), frame);
        CollectionAssert.AreEqual(Png(9), CoverArt.ReadEmbedded(Write("c.mp3", Concat(tag, FakeMp3Audio))));
    }

    [TestMethod]
    public void Id3v23_unsynchronised_tag()
    {
        // Slika sadrži FF E0 (JPEG): u neusklađenoj oznaci zapisano je kao FF 00 E0.
        byte[] image = Jpeg(5);
        byte[] raw = Id3v23(("APIC", Apic(3, image)));
        var body = new List<byte>();
        foreach (byte b in raw.Skip(10))
        {
            body.Add(b);
            if (b == 0xFF)
            {
                body.Add(0x00);
            }
        }

        byte[] tag = Concat(Encoding.ASCII.GetBytes("ID3"), [3, 0, 0x80], SyncSafe(body.Count), body.ToArray());
        CollectionAssert.AreEqual(image, CoverArt.ReadEmbedded(Write("d.mp3", Concat(tag, FakeMp3Audio))));
    }

    private static byte[] FlacPicture(int type, byte[] image) =>
        Concat(Be32(type), Be32(9), Encoding.ASCII.GetBytes("image/png"), Be32(0), Be32(300), Be32(300), Be32(24), Be32(0), Be32(image.Length), image);

    private static byte[] FlacBlock(int type, byte[] data, bool last) =>
        Concat([(byte)((last ? 0x80 : 0) | type), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length], data);

    [TestMethod]
    public void Flac_picture_block_even_after_an_id3_tag()
    {
        byte[] flac = Concat(Encoding.ASCII.GetBytes("fLaC"), FlacBlock(0, new byte[34], false), FlacBlock(4, new byte[20], false),
            FlacBlock(6, FlacPicture(4, Png(1)), false), FlacBlock(6, FlacPicture(3, Png(3)), true), [0xFF, 0xF8, 0, 0]);
        CollectionAssert.AreEqual(Png(3), CoverArt.ReadEmbedded(Write("e.flac", flac)));

        byte[] withId3 = Concat(Id3v23(("TIT2", Concat([0], Encoding.ASCII.GetBytes("x")))), flac);
        CollectionAssert.AreEqual(Png(3), CoverArt.ReadEmbedded(Write("f.flac", withId3)));
    }

    private static byte[] Atom(string type, params byte[][] children)
    {
        byte[] body = Concat(children);
        return Concat(Be32(body.Length + 8), Encoding.ASCII.GetBytes(type), body);
    }

    [TestMethod]
    public void Mp4_covr_when_moov_is_after_mdat()
    {
        byte[] data = Atom("data", Be32(14), Be32(0), Png(4));
        byte[] moov = Atom("moov", Atom("mvhd", new byte[100]),
            Atom("udta", Atom("meta", new byte[4], Atom("hdlr", new byte[25]), Atom("ilst", Atom("aART", Atom("data", Be32(1), Be32(0), Encoding.ASCII.GetBytes("x"))), Atom("covr", data)))));
        byte[] file = Concat(Atom("ftyp", Encoding.ASCII.GetBytes("M4A "), Be32(0)), Atom("mdat", new byte[5000]), moov);
        CollectionAssert.AreEqual(Png(4), CoverArt.ReadEmbedded(Write("g.m4a", file)));
    }

    private static byte[] OggPage(uint serial, uint sequence, byte headerType, byte[] segmentTable, byte[] body)
    {
        var header = new byte[27];
        Encoding.ASCII.GetBytes("OggS").CopyTo(header, 0);
        header[5] = headerType;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(14), serial);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(18), sequence);
        header[26] = (byte)segmentTable.Length;
        return Concat(header, segmentTable, body);
    }

    private static byte[] Lacing(int length, bool ends)
    {
        var table = new List<byte>();
        while (length >= 255)
        {
            table.Add(255);
            length -= 255;
        }

        if (ends)
        {
            table.Add((byte)length);
        }

        return table.ToArray();
    }

    [TestMethod]
    public void Ogg_opus_picture_comment_spanning_two_pages()
    {
        byte[] head = Concat(Encoding.ASCII.GetBytes("OpusHead"), new byte[11]);
        byte[] image = Concat(Png(6), new byte[900]); // dovoljno velika da komentar prijeđe na drugu stranicu
        string picture = Convert.ToBase64String(FlacPicture(3, image));
        byte[] comment = Encoding.ASCII.GetBytes("METADATA_BLOCK_PICTURE=" + picture);
        byte[] title = Encoding.ASCII.GetBytes("TITLE=Omot");
        byte[] vendor = Encoding.ASCII.GetBytes("test");
        byte[] le(int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); return b; }
        byte[] tags = Concat(Encoding.ASCII.GetBytes("OpusTags"), le(vendor.Length), vendor, le(2), le(title.Length), title, le(comment.Length), comment);

        // Paket komentara razdvojen na dvije stranice (prva završava punim segmentom 255).
        int split = 255 * 2;
        byte[] part1 = tags[..split], part2 = tags[split..];
        byte[] ogg = Concat(
            OggPage(77, 0, 2, Lacing(head.Length, true), head),
            OggPage(77, 1, 0, [255, 255], part1),
            OggPage(77, 2, 1, Lacing(part2.Length, true), part2),
            OggPage(77, 3, 0, [10], new byte[10]));
        Assert.IsTrue(tags.Length > split + 100, "test packet must span two pages");
        CollectionAssert.AreEqual(image, CoverArt.ReadEmbedded(Write("h.opus", ogg)));
    }

    [TestMethod]
    public void Folder_image_is_used_when_nothing_is_embedded_and_embedded_wins()
    {
        string plain = Write(Path.Combine("album", "01.mp3"), FakeMp3Audio);
        Assert.IsNull(CoverArt.Read(plain));

        Write(Path.Combine("album", "Folder.JPG"), Jpeg(1));
        Write(Path.Combine("album", "cover.png"), Png(2));
        CoverArtImage? folder = CoverArt.Read(plain);
        Assert.IsNotNull(folder);
        Assert.AreEqual("folder", folder.Source);
        CollectionAssert.AreEqual(Png(2), folder.Data, "cover beats folder");

        string tagged = Write(Path.Combine("album", "02.mp3"), Concat(Id3v23(("APIC", Apic(3, Png(8)))), FakeMp3Audio));
        CoverArtImage? embedded = CoverArt.Read(tagged);
        Assert.AreEqual("embedded", embedded!.Source);
        CollectionAssert.AreEqual(Png(8), embedded.Data);
    }

    [TestMethod]
    public void Broken_or_strange_files_give_no_cover_and_never_throw()
    {
        var random = new Random(1234);
        byte[] valid = Concat(Id3v23(("APIC", Apic(3, Png(2)))), FakeMp3Audio);
        for (int cut = 0; cut < valid.Length; cut += 3)
        {
            _ = CoverArt.ReadEmbedded(Write($"cut{cut}.mp3", valid[..cut]));
        }

        string[] prefixes = ["ID3", "fLaC", "OggS", "\0\0\0\u0018ftyp"];
        for (int p = 0; p < prefixes.Length; p++)
        {
            for (int i = 0; i < 40; i++)
            {
                byte[] junk = new byte[random.Next(0, 400)];
                random.NextBytes(junk);
                byte[] file = Concat(Encoding.ASCII.GetBytes(prefixes[p]), junk);
                _ = CoverArt.Read(Write($"junk{p}-{i}.bin", file));
            }
        }

        // Okvir koji kaže da je veći od cijele oznake; slika koja nije slika.
        byte[] lying = Concat(Encoding.ASCII.GetBytes("ID3"), [3, 0, 0], SyncSafe(20), Encoding.ASCII.GetBytes("APIC"), Be32(int.MaxValue), [0, 0], new byte[10]);
        Assert.IsNull(CoverArt.ReadEmbedded(Write("lying.mp3", lying)));
        Assert.IsNull(CoverArt.ReadEmbedded(Write("notimage.mp3", Concat(Id3v23(("APIC", Apic(3, Encoding.ASCII.GetBytes("nije slika ovo je tekst")))), FakeMp3Audio))));
        Assert.IsNull(CoverArt.Read(Path.Combine(_dir, "ne-postoji.mp3")));
    }
}
