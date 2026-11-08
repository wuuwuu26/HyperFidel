using System.Text;

namespace HyperFidel.Library;

/// <summary>
/// Reads the ID3v2.3 tag that DSDIFF (DFF) files carry inside their <c>DIIN</c> chunk.
/// TagLib has no DSDIFF container reader, so <see cref="BuildRecord"/> otherwise ends up
/// with a record that has no title, artist, album, track number or embedded cover.
/// </summary>
internal static class DffTagReader
{
    /// <summary>Fills tag-derived fields of <paramref name="record"/> when a usable tag exists.</summary>
    /// <param name="record">Record being built for the source file.</param>
    /// <param name="path">Physical DFF source file.</param>
    internal static void Apply(TrackRecord record, string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            var tag = FindTag(data);
            if (tag < 0) return;

            var size = SyncSafe(data, tag + 6);
            var end = Math.Min(data.Length, tag + 10 + size);
            var pos = tag + 10;
            while (pos + 10 <= end)
            {
                var id = Encoding.ASCII.GetString(data, pos, 4);
                if (!IsFrameId(id)) break;
                var frameSize = (data[pos + 4] << 24) | (data[pos + 5] << 16) |
                                (data[pos + 6] << 8) | data[pos + 7];
                var payload = pos + 10;
                if (frameSize <= 0 || payload + frameSize > end) break;

                switch (id)
                {
                    case "TIT2": record.Title = Text(data, payload, frameSize); break;
                    case "TPE1": record.Artist = Text(data, payload, frameSize); break;
                    case "TPE2": record.AlbumArtist = Text(data, payload, frameSize); break;
                    case "TALB": record.Album = Text(data, payload, frameSize); break;
                    case "TCON": record.Genre = Text(data, payload, frameSize); break;
                    case "TCOM": record.Composer = Text(data, payload, frameSize); break;
                    case "TEXT": record.Lyricist = Text(data, payload, frameSize); break;
                    case "TYER":
                    case "TDRC":
                        if (int.TryParse(LeadingNumber(Text(data, payload, frameSize)), out var year))
                            record.Year = year;
                        break;
                    case "TRCK":
                        (record.TrackNumber, record.TrackTotal) = Pair(Text(data, payload, frameSize));
                        break;
                    case "TPOS":
                        (record.DiscNumber, record.DiscTotal) = Pair(Text(data, payload, frameSize));
                        break;
                    case "USLT":
                        record.Lyrics = UnsyncLyrics(data, payload, frameSize);
                        break;
                    case "APIC":
                        ReadPicture(record, data, payload, frameSize);
                        break;
                }
                pos = payload + frameSize;
            }
        }
        catch
        {
            // A malformed or unreadable tag must never fail the scan.
        }
    }

    private static int FindTag(byte[] data)
    {
        // ID3v2.3 header: "ID3", 0x03, 0x00. DSDIFF stores it in the trailing DIIN chunk.
        for (var i = 0; i + 10 <= data.Length; i++)
        {
            if (data[i] == (byte)'I' && data[i + 1] == (byte)'D' && data[i + 2] == (byte)'3' &&
                data[i + 3] == 3 && data[i + 4] == 0)
                return i;
        }
        return -1;
    }

    private static int SyncSafe(byte[] data, int offset) =>
        ((data[offset] & 0x7F) << 21) | ((data[offset + 1] & 0x7F) << 14) |
        ((data[offset + 2] & 0x7F) << 7) | (data[offset + 3] & 0x7F);

    private static bool IsFrameId(string id) =>
        id.Length == 4 && id.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9');

    private static string Text(byte[] data, int offset, int length)
    {
        if (length <= 1) return string.Empty;
        var encoding = data[offset];
        var payload = new ReadOnlySpan<byte>(data, offset + 1, length - 1);
        var value = encoding switch
        {
            1 => Encoding.Unicode.GetString(payload),
            2 => Encoding.BigEndianUnicode.GetString(payload),
            3 => Encoding.UTF8.GetString(payload),
            _ => Encoding.Latin1.GetString(payload)
        };
        return value.TrimEnd('\0').Trim();
    }

    private static string UnsyncLyrics(byte[] data, int offset, int length)
    {
        if (length <= 4) return string.Empty;
        var encoding = data[offset];
        var p = offset + 1;
        var end = offset + length;
        // language (3 bytes) then a terminated description
        p += 3;
        while (p < end && data[p] != 0)
        {
            p += encoding is 1 or 2 ? 2 : 1;
            if (encoding is 1 or 2 && p > offset + 1 && data[p - 1] == 0 && data[p] == 0) { p++; break; }
        }
        if (p < end) p++;
        if (p >= end) return string.Empty;
        var payload = new ReadOnlySpan<byte>(data, p, end - p);
        var value = encoding switch
        {
            1 => Encoding.Unicode.GetString(payload),
            2 => Encoding.BigEndianUnicode.GetString(payload),
            3 => Encoding.UTF8.GetString(payload),
            _ => Encoding.Latin1.GetString(payload)
        };
        return value.TrimEnd('\0').Trim();
    }

    private static void ReadPicture(TrackRecord record, byte[] data, int offset, int length)
    {
        if (length <= 4) return;
        var end = offset + length;
        var encoding = data[offset];
        var p = offset + 1;
        var mimeEnd = p;
        while (mimeEnd < end && data[mimeEnd] != 0) mimeEnd++;
        if (mimeEnd >= end) return;
        var mime = Encoding.ASCII.GetString(data, p, mimeEnd - p);
        p = mimeEnd + 1;
        if (p >= end) return;
        p++; // picture type
        // description, terminated by a single 0x00 (Latin1/UTF8) or 0x0000 (UTF-16)
        while (p < end && data[p] != 0) p += encoding is 1 or 2 ? 2 : 1;
        p += encoding is 1 or 2 ? 2 : 1;
        if (p >= end) return;

        var image = new byte[end - p];
        Array.Copy(data, p, image, 0, image.Length);
        if (image.Length == 0) return;
        record.HasCover = true;
        record.CoverMimeType = string.IsNullOrWhiteSpace(mime) ? "image/jpeg" : mime;
        record.CoverData = image;
    }

    private static string LeadingNumber(string value)
    {
        var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
        return digits;
    }

    private static (int? Number, int? Total) Pair(string value)
    {
        var parts = value.Split('/', 2);
        int? number = int.TryParse(parts[0].Trim(), out var n) ? n : null;
        int? total = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var t) ? t : null;
        return (number, total);
    }
}
