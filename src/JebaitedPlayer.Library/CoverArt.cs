using System.Buffers.Binary;
using System.Text;

namespace JebaitedPlayer.Library;

/// <summary>Omot pjesme: slika i odakle je (ugrađena u datoteku ili slika u mapi).</summary>
public sealed record CoverArtImage(byte[] Data, string Source);

/// <summary>
/// 2.1.12: omot albuma bez dodatne biblioteke, samo čitanjem. Ugrađena slika iz ID3v2 (MP3; APIC/PIC, v2.2–2.4),
/// FLAC (PICTURE), MP4/M4A (covr) i Ogg Vorbis/Opus (METADATA_BLOCK_PICTURE); prednost ima prednja naslovnica.
/// Bez ugrađene: cover/folder/front/album(art).jpg|png u mapi pjesme. Datoteke se nikad ne mijenjaju; svaka greška
/// ili neobična datoteka znači „nema omota” (nikad iznimka prema van).
/// </summary>
public static class CoverArt
{
    /// <summary>Najveća slika koja se prihvaća.</summary>
    public const int MaxImageBytes = 20 * 1024 * 1024;

    /// <summary>Najviše bajtova oznaka koji se čitaju (ID3 na početku, moov u MP4, Ogg zaglavlja).</summary>
    public const int MaxTagBytes = 64 * 1024 * 1024;

    private const int FrontCover = 3;

    private static readonly string[] FolderNames = ["cover", "folder", "front", "album", "albumart", "albumartsmall"];
    private static readonly string[] FolderExtensions = [".jpg", ".jpeg", ".png"];

    /// <summary>Ugrađeni omot ili slika u mapi; null ako nema ničega upotrebljivog.</summary>
    public static CoverArtImage? Read(string path)
    {
        try
        {
            if (ReadEmbedded(path) is { } embedded)
            {
                return new CoverArtImage(embedded, "embedded");
            }

            if (FindFolderImage(path) is { } image)
            {
                var info = new FileInfo(image);
                if (info.Length is > 0 and <= MaxImageBytes)
                {
                    byte[] data = File.ReadAllBytes(image);
                    if (IsImage(data))
                    {
                        return new CoverArtImage(data, "folder");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }

        return null;
    }

    /// <summary>Slika ugrađena u datoteku (prema sadržaju, ne nastavku); null ako je nema.</summary>
    public static byte[]? ReadEmbedded(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            Span<byte> head = stackalloc byte[12];
            int n = ReadAtMost(file, head);
            if (n < 4)
            {
                return null;
            }

            byte[]? picture = null;
            long afterId3 = 0;
            if (head[0] == 'I' && head[1] == 'D' && head[2] == '3' && n >= 10)
            {
                picture = ReadId3(file, out afterId3);
            }

            if (picture is null)
            {
                file.Position = afterId3;
                n = ReadAtMost(file, head);
                if (n >= 4 && head[..4].SequenceEqual("fLaC"u8))
                {
                    picture = ReadFlac(file);
                }
                else if (n >= 4 && head[..4].SequenceEqual("OggS"u8))
                {
                    file.Position = afterId3;
                    picture = ReadOgg(file);
                }
                else if (n >= 8 && head[4..8].SequenceEqual("ftyp"u8))
                {
                    file.Position = afterId3;
                    picture = ReadMp4(file);
                }
            }

            return picture is not null && picture.Length <= MaxImageBytes && IsImage(picture) ? picture : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                       or FormatException or OverflowException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Slika omota u mapi pjesme (cover, folder, front, album…; jpg ili png), bez obzira na velika i mala slova.</summary>
    public static string? FindFolderImage(string audioPath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(audioPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                return null;
            }

            var files = Directory.EnumerateFiles(dir)
                .Where(f => FolderExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToDictionary(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant(), f => f, StringComparer.Ordinal);
            foreach (string name in FolderNames)
            {
                if (files.TryGetValue(name, out string? found))
                {
                    return found;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return null;
    }

    /// <summary>JPEG, PNG, GIF ili BMP (WPF ih dekodira bez dodataka).</summary>
    public static bool IsImage(ReadOnlySpan<byte> data) =>
        data.Length > 8 && (
            (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) ||
            data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ||
            data[..4].SequenceEqual("GIF8"u8) ||
            (data[0] == 'B' && data[1] == 'M'));

    // --- ID3v2 -----------------------------------------------------------------------------------------------------

    private static byte[]? ReadId3(FileStream file, out long tagEnd)
    {
        file.Position = 0;
        var header = new byte[10];
        tagEnd = 0;
        if (ReadAtMost(file, header) < 10)
        {
            return null;
        }

        int major = header[3];
        byte flags = header[5];
        int size = SyncSafe(header.AsSpan(6, 4));
        bool footer = major == 4 && (flags & 0x10) != 0;
        tagEnd = 10L + size + (footer ? 10 : 0);
        if (major is < 2 or > 4 || size <= 0 || size > MaxTagBytes)
        {
            return null;
        }

        byte[] tag = new byte[size];
        if (ReadAtMost(file, tag) < size)
        {
            return null;
        }

        // v2.2/2.3: neusklađenost (unsynchronisation) vrijedi za cijelu oznaku; v2.4 je označena po okviru.
        if ((flags & 0x80) != 0 && major < 4)
        {
            tag = RemoveUnsync(tag);
        }

        int pos = 0;
        if ((flags & 0x40) != 0 && major >= 3 && tag.Length >= 4)
        {
            int ext = major == 4 ? SyncSafe(tag.AsSpan(0, 4)) : 4 + (int)BinaryPrimitives.ReadUInt32BigEndian(tag);
            pos = ext is > 0 and < int.MaxValue ? ext : tag.Length;
        }

        int idLength = major == 2 ? 3 : 4;
        int headerLength = major == 2 ? 6 : 10;
        byte[]? first = null;
        while (pos + headerLength <= tag.Length && tag[pos] != 0)
        {
            string id = Encoding.ASCII.GetString(tag, pos, idLength);
            int frameSize = major switch
            {
                2 => (tag[pos + 3] << 16) | (tag[pos + 4] << 8) | tag[pos + 5],
                3 => (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(tag.AsSpan(pos + 4)), int.MaxValue),
                _ => SyncSafe(tag.AsSpan(pos + 4, 4)),
            };
            byte formatFlags = major == 2 ? (byte)0 : tag[pos + 9];
            int start = pos + headerLength;
            if (frameSize <= 0 || frameSize > tag.Length - start)
            {
                break;
            }

            pos = start + frameSize;
            if (id is not ("APIC" or "PIC"))
            {
                continue;
            }

            ReadOnlySpan<byte> body = tag.AsSpan(start, frameSize);
            if (major == 3 && (formatFlags & 0xC0) != 0)
            {
                continue; // stisnut ili kriptiran okvir
            }

            if (major == 3 && (formatFlags & 0x20) != 0)
            {
                body = body[1..]; // identifikator grupe
            }

            if (major == 4)
            {
                if ((formatFlags & 0x0C) != 0)
                {
                    continue; // stisnut ili kriptiran okvir
                }

                if ((formatFlags & 0x40) != 0 && body.Length > 0)
                {
                    body = body[1..];
                }

                if ((formatFlags & 0x01) != 0 && body.Length >= 4)
                {
                    body = body[4..]; // duljina podataka
                }

                if ((formatFlags & 0x02) != 0)
                {
                    body = RemoveUnsync(body.ToArray());
                }
            }

            if (ParseApic(body, id == "PIC") is { } picture)
            {
                if (picture.Type == FrontCover)
                {
                    return picture.Data;
                }

                first ??= picture.Data;
            }
        }

        return first;
    }

    private static (int Type, byte[] Data)? ParseApic(ReadOnlySpan<byte> body, bool v22)
    {
        if (body.Length < 4)
        {
            return null;
        }

        byte encoding = body[0];
        int pos = 1;
        if (v22)
        {
            pos += 3; // oblik slike, npr. "JPG"
        }
        else
        {
            int mimeEnd = body[pos..].IndexOf((byte)0);
            if (mimeEnd < 0)
            {
                return null;
            }

            pos += mimeEnd + 1;
        }

        if (pos >= body.Length)
        {
            return null;
        }

        int type = body[pos++];
        // Opis: kraj je 0 (Latin-1, UTF-8) ili 00 00 na parnom mjestu (UTF-16).
        if (encoding is 1 or 2)
        {
            int i = pos;
            while (i + 1 < body.Length && !(body[i] == 0 && body[i + 1] == 0))
            {
                i += 2;
            }

            pos = i + 2;
        }
        else
        {
            int end = body[pos..].IndexOf((byte)0);
            if (end < 0)
            {
                return null;
            }

            pos += end + 1;
        }

        return pos < body.Length ? (type, body[pos..].ToArray()) : null;
    }

    // --- FLAC ------------------------------------------------------------------------------------------------------

    private static byte[]? ReadFlac(FileStream file)
    {
        // Položaj je odmah iza "fLaC" (pročitano je 12 bajtova, pa se vraćamo).
        file.Position -= Math.Min(file.Position, 8);
        Span<byte> blockHeader = stackalloc byte[4];
        byte[]? first = null;
        long budget = MaxTagBytes;
        while (ReadAtMost(file, blockHeader) == 4)
        {
            bool last = (blockHeader[0] & 0x80) != 0;
            int type = blockHeader[0] & 0x7F;
            int length = (blockHeader[1] << 16) | (blockHeader[2] << 8) | blockHeader[3];
            budget -= length;
            if (budget < 0)
            {
                break;
            }

            if (type == 6)
            {
                byte[] block = new byte[length];
                if (ReadAtMost(file, block) < length)
                {
                    break;
                }

                if (ParseFlacPicture(block) is { } picture)
                {
                    if (picture.Type == FrontCover)
                    {
                        return picture.Data;
                    }

                    first ??= picture.Data;
                }
            }
            else
            {
                file.Seek(length, SeekOrigin.Current);
            }

            if (last || type == 127)
            {
                break;
            }
        }

        return first;
    }

    /// <summary>FLAC METADATA_BLOCK_PICTURE (i u FLAC-u i, kao base64, u Ogg komentarima).</summary>
    private static (int Type, byte[] Data)? ParseFlacPicture(ReadOnlySpan<byte> block)
    {
        int pos = 0;
        bool Read32(ReadOnlySpan<byte> b, out uint value)
        {
            value = 0;
            if (pos + 4 > b.Length)
            {
                return false;
            }

            value = BinaryPrimitives.ReadUInt32BigEndian(b[pos..]);
            pos += 4;
            return true;
        }

        if (!Read32(block, out uint type) || !Read32(block, out uint mime) || mime > (uint)(block.Length - pos))
        {
            return null;
        }

        pos += (int)mime;
        if (!Read32(block, out uint description) || description > (uint)(block.Length - pos))
        {
            return null;
        }

        pos += (int)description;
        pos += 16; // širina, visina, dubina boje, broj boja
        if (!Read32(block, out uint length) || length == 0 || length > (uint)(block.Length - pos))
        {
            return null;
        }

        return ((int)type, block.Slice(pos, (int)length).ToArray());
    }

    // --- MP4 / M4A -------------------------------------------------------------------------------------------------

    private static byte[]? ReadMp4(FileStream file)
    {
        // moov može biti i iza mdat (na kraju datoteke): idemo po atomima najviše razine.
        long fileLength = file.Length;
        long pos = file.Position;
        Span<byte> header = stackalloc byte[16];
        for (int guard = 0; guard < 1000 && pos + 8 <= fileLength; guard++)
        {
            file.Position = pos;
            if (ReadAtMost(file, header[..8]) < 8)
            {
                return null;
            }

            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            int headerLength = 8;
            if (size == 1)
            {
                if (ReadAtMost(file, header[8..16]) < 8)
                {
                    return null;
                }

                size = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(header[8..]), long.MaxValue);
                headerLength = 16;
            }
            else if (size == 0)
            {
                size = fileLength - pos;
            }

            if (size < headerLength)
            {
                return null;
            }

            if (header[4..8].SequenceEqual("moov"u8))
            {
                long body = size - headerLength;
                if (body > MaxTagBytes)
                {
                    return null;
                }

                byte[] moov = new byte[body];
                return ReadAtMost(file, moov) == body ? FindCovr(moov) : null;
            }

            pos += size;
        }

        return null;
    }

    private static byte[]? FindCovr(byte[] moov)
    {
        // moov → udta → meta (puni atom: 4 bajta verzije) → ilst → covr → data.
        if (Child(moov, 0, moov.Length, "udta") is not { } udta ||
            Child(moov, udta.Start, udta.End, "meta") is not { } meta ||
            Child(moov, meta.Start + 4, meta.End, "ilst") is not { } ilst ||
            Child(moov, ilst.Start, ilst.End, "covr") is not { } covr)
        {
            return null;
        }

        if (Child(moov, covr.Start, covr.End, "data") is not { } data || data.End - data.Start <= 8)
        {
            return null;
        }

        // data: 4 bajta vrste (13 JPEG, 14 PNG), 4 bajta jezika, zatim slika.
        return moov.AsSpan(data.Start + 8, data.End - data.Start - 8).ToArray();
    }

    private static (int Start, int End)? Child(byte[] buffer, int start, int end, string type)
    {
        int pos = start;
        while (pos + 8 <= end)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(pos));
            int headerLength = 8;
            if (size == 1 && pos + 16 <= end)
            {
                size = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(buffer.AsSpan(pos + 8)), long.MaxValue);
                headerLength = 16;
            }
            else if (size == 0)
            {
                size = end - pos;
            }

            if (size < headerLength || size > end - pos)
            {
                return null;
            }

            if (Encoding.ASCII.GetString(buffer, pos + 4, 4) == type)
            {
                return (pos + headerLength, pos + (int)size);
            }

            pos += (int)size;
        }

        return null;
    }

    // --- Ogg Vorbis / Opus -----------------------------------------------------------------------------------------

    private static byte[]? ReadOgg(FileStream file)
    {
        // Drugi paket prvog toka su komentari (Vorbis "\x03vorbis", Opus "OpusTags"); može se protezati preko više stranica.
        var packet = new MemoryStream();
        int packetIndex = 0;
        uint? serial = null;
        Span<byte> page = stackalloc byte[27];
        byte[] segments = new byte[255];
        for (int pages = 0; pages < 10_000; pages++)
        {
            if (ReadAtMost(file, page) < 27 || !page[..4].SequenceEqual("OggS"u8))
            {
                return null;
            }

            uint pageSerial = BinaryPrimitives.ReadUInt32LittleEndian(page[14..]);
            int count = page[26];
            if (ReadAtMost(file, segments.AsSpan(0, count)) < count)
            {
                return null;
            }

            int bodyLength = 0;
            for (int i = 0; i < count; i++)
            {
                bodyLength += segments[i];
            }

            byte[] body = new byte[bodyLength];
            if (ReadAtMost(file, body) < bodyLength)
            {
                return null;
            }

            serial ??= pageSerial;
            if (pageSerial != serial)
            {
                continue;
            }

            int offset = 0;
            for (int i = 0; i < count; i++)
            {
                int length = segments[i];
                if (packetIndex == 1)
                {
                    packet.Write(body, offset, length);
                    if (packet.Length > MaxTagBytes)
                    {
                        return null;
                    }
                }

                offset += length;
                if (length < 255)
                {
                    // Kraj paketa.
                    if (packetIndex == 1)
                    {
                        return ParseOggComments(packet.ToArray());
                    }

                    packetIndex++;
                }
            }
        }

        return null;
    }

    private static byte[]? ParseOggComments(byte[] packet)
    {
        int pos = packet.AsSpan().StartsWith("\u0003vorbis"u8) ? 7 : packet.AsSpan().StartsWith("OpusTags"u8) ? 8 : -1;
        if (pos < 0 || pos + 4 > packet.Length)
        {
            return null;
        }

        uint vendor = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(pos));
        if (vendor > (uint)(packet.Length - pos - 4))
        {
            return null;
        }

        pos += 4 + (int)vendor;
        if (pos + 4 > packet.Length)
        {
            return null;
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(pos));
        pos += 4;
        byte[]? first = null;
        for (uint c = 0; c < count && pos + 4 <= packet.Length; c++)
        {
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(pos));
            pos += 4;
            if (length > (uint)(packet.Length - pos))
            {
                return first;
            }

            ReadOnlySpan<byte> comment = packet.AsSpan(pos, (int)length);
            pos += (int)length;
            const string key = "METADATA_BLOCK_PICTURE=";
            if (comment.Length > key.Length && Encoding.ASCII.GetString(comment[..key.Length]).Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                byte[] block = Convert.FromBase64String(Encoding.ASCII.GetString(comment[key.Length..]));
                if (ParseFlacPicture(block) is { } picture)
                {
                    if (picture.Type == FrontCover)
                    {
                        return picture.Data;
                    }

                    first ??= picture.Data;
                }
            }
        }

        return first;
    }

    // --- Pomoćno -----------------------------------------------------------------------------------------------------

    private static int ReadAtMost(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = stream.Read(buffer[total..]);
            if (n == 0)
            {
                break;
            }

            total += n;
        }

        return total;
    }

    private static int SyncSafe(ReadOnlySpan<byte> b) => ((b[0] & 0x7F) << 21) | ((b[1] & 0x7F) << 14) | ((b[2] & 0x7F) << 7) | (b[3] & 0x7F);

    /// <summary>ID3 „unsynchronisation”: svaki par FF 00 vraća se u FF.</summary>
    internal static byte[] RemoveUnsync(byte[] data)
    {
        var result = new byte[data.Length];
        int n = 0;
        for (int i = 0; i < data.Length; i++)
        {
            result[n++] = data[i];
            if (data[i] == 0xFF && i + 1 < data.Length && data[i + 1] == 0x00)
            {
                i++;
            }
        }

        Array.Resize(ref result, n);
        return result;
    }
}
