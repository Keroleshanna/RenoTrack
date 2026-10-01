namespace RenoTrack.Website.Site;

/// <summary>Why an image file was refused. <see cref="None"/> means it is acceptable.</summary>
public enum ImageRefusal
{
    None,

    /// <summary>Neither a JPEG nor a WebP signature.</summary>
    UnrecognisedFormat,

    /// <summary>The file ends before its structure says it should.</summary>
    Truncated,

    /// <summary>The structure is inconsistent: bad lengths, a missing frame, a duplicate bitstream.</summary>
    Malformed,

    /// <summary>EXIF, XMP, IPTC, a comment or any other metadata the site does not publish.</summary>
    Metadata,

    /// <summary>A valid but unsupported encoding: arithmetic or lossless JPEG, animation, an unknown chunk.</summary>
    Unsupported,

    /// <summary>Bytes after the end of the image, where anything could be hidden.</summary>
    TrailingData,
}

/// <summary>The result of inspecting one file. Dimensions are meaningful only when <see cref="Refusal"/> is <see cref="ImageRefusal.None"/>.</summary>
/// <param name="Detail">A fixed description of the structural reason, never file content.</param>
public sealed record ImageInspection(MediaFormat? Format, int Width, int Height, ImageRefusal Refusal, string? Detail)
{
    public bool IsAcceptable => Refusal == ImageRefusal.None;

    internal static ImageInspection Refuse(ImageRefusal refusal, string detail, MediaFormat? format = null) =>
        new(format, 0, 0, refusal, detail);
}

/// <summary>
/// Byte-level verification of a published derivative: format, pixel size, and the absence of every kind of
/// metadata (<b>D106</b>, S5-3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Hand-written and allowlist-shaped, with no image library in the product.</b> Rather than hunting for known
/// metadata, it accepts only the structures a clean derivative needs and refuses everything else. For JPEG that is
/// the JFIF header, an optional ICC colour profile, quantisation and Huffman tables, a restart interval, the frame
/// and the scans. For WebP it is a single bitstream with optional alpha and ICC colour profile. EXIF lives in the
/// JPEG APP1 segment and the WebP <c>EXIF</c> chunk, and GPS coordinates live inside EXIF, so refusing EXIF
/// entirely closes accidental location leaks without parsing it. XMP, IPTC, Photoshop resources, comments,
/// thumbnails and anything appended after the image are refused the same way.
/// </para>
/// <para>
/// <b>Shared with the preparation tool</b>, which links this file and runs it on its own output, so the tool
/// cannot produce a file the site would refuse.
/// </para>
/// </remarks>
public static class ImageFileInspector
{
    public static ImageInspection Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return InspectJpeg(bytes);
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return InspectWebP(bytes);
        }

        return ImageInspection.Refuse(ImageRefusal.UnrecognisedFormat, "neither a JPEG nor a WebP signature");
    }

    // ---- JPEG ---------------------------------------------------------------------------------------------

    private static ImageInspection InspectJpeg(ReadOnlySpan<byte> bytes)
    {
        const MediaFormat format = MediaFormat.Jpeg;
        var position = 2;
        var width = 0;
        var height = 0;
        var frameSeen = false;
        var scanSeen = false;

        while (true)
        {
            if (position >= bytes.Length)
            {
                return ImageInspection.Refuse(ImageRefusal.Truncated, "ends before the end-of-image marker", format);
            }

            if (bytes[position] != 0xFF)
            {
                if (!scanSeen)
                {
                    return ImageInspection.Refuse(ImageRefusal.Malformed, "expected a marker", format);
                }

                // Entropy-coded scan data: skip to the next marker, honouring byte stuffing (FF 00) and
                // restart markers (FF D0–D7), which are part of the scan.
                position = SkipEntropyData(bytes, position);
                continue;
            }

            // Fill bytes: any number of FF may precede a marker.
            while (position < bytes.Length && bytes[position] == 0xFF)
            {
                position++;
            }

            if (position >= bytes.Length)
            {
                return ImageInspection.Refuse(ImageRefusal.Truncated, "ends inside a marker", format);
            }

            var marker = bytes[position++];

            if (marker == 0xD9)
            {
                if (!frameSeen || !scanSeen)
                {
                    return ImageInspection.Refuse(ImageRefusal.Malformed, "no frame or scan before the end of image", format);
                }

                return position == bytes.Length
                    ? new ImageInspection(format, width, height, ImageRefusal.None, null)
                    : ImageInspection.Refuse(ImageRefusal.TrailingData, "bytes after the end-of-image marker", format);
            }

            if (marker is >= 0xD0 and <= 0xD7)
            {
                if (!scanSeen)
                {
                    return ImageInspection.Refuse(ImageRefusal.Malformed, "restart marker outside a scan", format);
                }

                continue;
            }

            if (position + 2 > bytes.Length)
            {
                return ImageInspection.Refuse(ImageRefusal.Truncated, "ends inside a segment length", format);
            }

            var length = (bytes[position] << 8) | bytes[position + 1];
            if (length < 2 || position + length > bytes.Length)
            {
                return ImageInspection.Refuse(
                    position + length > bytes.Length ? ImageRefusal.Truncated : ImageRefusal.Malformed,
                    "segment length out of range",
                    format);
            }

            var payload = bytes.Slice(position + 2, length - 2);

            switch (marker)
            {
                case 0xE0:
                    // APP0: only the plain JFIF header. JFXX carries a thumbnail, which is refused.
                    if (!payload.StartsWith("JFIF\0"u8))
                    {
                        return ImageInspection.Refuse(ImageRefusal.Metadata, "APP0 segment other than JFIF", format);
                    }

                    break;

                case 0xE1:
                    return ImageInspection.Refuse(ImageRefusal.Metadata, "APP1 segment (EXIF or XMP)", format);

                case 0xE2:
                    if (!payload.StartsWith("ICC_PROFILE\0"u8))
                    {
                        return ImageInspection.Refuse(ImageRefusal.Metadata, "APP2 segment other than an ICC profile", format);
                    }

                    break;

                case >= 0xE3 and <= 0xEF:
                    return ImageInspection.Refuse(ImageRefusal.Metadata, "application segment APP3–APP15 (e.g. IPTC)", format);

                case 0xFE:
                    return ImageInspection.Refuse(ImageRefusal.Metadata, "comment segment", format);

                case 0xDB or 0xC4 or 0xDD:
                    // Quantisation tables, Huffman tables, restart interval.
                    break;

                case 0xC0 or 0xC1 or 0xC2:
                    if (frameSeen)
                    {
                        return ImageInspection.Refuse(ImageRefusal.Malformed, "more than one frame", format);
                    }

                    if (payload.Length < 6)
                    {
                        return ImageInspection.Refuse(ImageRefusal.Malformed, "frame header too short", format);
                    }

                    height = (payload[1] << 8) | payload[2];
                    width = (payload[3] << 8) | payload[4];
                    var components = payload[5];
                    if (width == 0 || height == 0 || components is not (1 or 3) || payload.Length != 6 + (3 * components))
                    {
                        return ImageInspection.Refuse(ImageRefusal.Malformed, "invalid frame header", format);
                    }

                    frameSeen = true;
                    break;

                case >= 0xC3 and <= 0xCF:
                    // SOF3, SOF5–7, SOF9–11, SOF13–15 (lossless, hierarchical, arithmetic) and DAC/JPG.
                    return ImageInspection.Refuse(ImageRefusal.Unsupported, "unsupported JPEG coding process", format);

                case 0xDA:
                    if (!frameSeen)
                    {
                        return ImageInspection.Refuse(ImageRefusal.Malformed, "scan before frame", format);
                    }

                    scanSeen = true;
                    break;

                default:
                    return ImageInspection.Refuse(ImageRefusal.Unsupported, "unexpected marker", format);
            }

            position += length;
        }
    }

    private static int SkipEntropyData(ReadOnlySpan<byte> bytes, int position)
    {
        while (position < bytes.Length)
        {
            if (bytes[position] != 0xFF)
            {
                position++;
                continue;
            }

            if (position + 1 >= bytes.Length)
            {
                return bytes.Length;
            }

            var next = bytes[position + 1];
            if (next == 0x00 || next is >= 0xD0 and <= 0xD7)
            {
                position += 2;
                continue;
            }

            return position;
        }

        return position;
    }

    // ---- WebP ---------------------------------------------------------------------------------------------

    private static ImageInspection InspectWebP(ReadOnlySpan<byte> bytes)
    {
        const MediaFormat format = MediaFormat.WebP;

        var riffSize = ReadUInt32(bytes[4..8]);
        if (riffSize + 8L != bytes.Length)
        {
            return ImageInspection.Refuse(
                riffSize + 8L > bytes.Length ? ImageRefusal.Truncated : ImageRefusal.TrailingData,
                "RIFF size does not match the file length",
                format);
        }

        var position = 12;
        var width = 0;
        var height = 0;
        var bitstreams = 0;
        var extended = false;

        while (position < bytes.Length)
        {
            if (position + 8 > bytes.Length)
            {
                return ImageInspection.Refuse(ImageRefusal.Truncated, "ends inside a chunk header", format);
            }

            var fourCc = bytes.Slice(position, 4);
            var size = ReadUInt32(bytes.Slice(position + 4, 4));
            var padded = size + (size & 1);
            if (position + 8L + padded > bytes.Length)
            {
                return ImageInspection.Refuse(ImageRefusal.Truncated, "chunk extends past the end of the file", format);
            }

            var data = bytes.Slice(position + 8, (int)size);

            if (fourCc.SequenceEqual("VP8 "u8))
            {
                // Lossy: 3-byte frame tag, start code 9D 01 2A, then 14-bit width and height.
                if (data.Length < 10 || data[3] != 0x9D || data[4] != 0x01 || data[5] != 0x2A)
                {
                    return ImageInspection.Refuse(ImageRefusal.Malformed, "invalid VP8 frame header", format);
                }

                width = ((data[7] << 8) | data[6]) & 0x3FFF;
                height = ((data[9] << 8) | data[8]) & 0x3FFF;
                bitstreams++;
            }
            else if (fourCc.SequenceEqual("VP8L"u8))
            {
                // Lossless: signature 0x2F, then 14 bits (width - 1) and 14 bits (height - 1), little-endian.
                if (data.Length < 5 || data[0] != 0x2F)
                {
                    return ImageInspection.Refuse(ImageRefusal.Malformed, "invalid VP8L header", format);
                }

                var bits = (uint)(data[1] | (data[2] << 8) | (data[3] << 16) | (data[4] << 24));
                width = (int)(bits & 0x3FFF) + 1;
                height = (int)((bits >> 14) & 0x3FFF) + 1;
                bitstreams++;
            }
            else if (fourCc.SequenceEqual("VP8X"u8))
            {
                if (position != 12 || data.Length < 10)
                {
                    return ImageInspection.Refuse(ImageRefusal.Malformed, "misplaced or short VP8X chunk", format);
                }

                var flags = data[0];
                if ((flags & 0x08) != 0 || (flags & 0x04) != 0)
                {
                    return ImageInspection.Refuse(ImageRefusal.Metadata, "VP8X declares EXIF or XMP metadata", format);
                }

                if ((flags & 0x02) != 0)
                {
                    return ImageInspection.Refuse(ImageRefusal.Unsupported, "animated WebP", format);
                }

                extended = true;
            }
            else if (fourCc.SequenceEqual("EXIF"u8) || fourCc.SequenceEqual("XMP "u8))
            {
                return ImageInspection.Refuse(ImageRefusal.Metadata, "EXIF or XMP chunk", format);
            }
            else if (fourCc.SequenceEqual("ANIM"u8) || fourCc.SequenceEqual("ANMF"u8))
            {
                return ImageInspection.Refuse(ImageRefusal.Unsupported, "animated WebP", format);
            }
            else if ((fourCc.SequenceEqual("ICCP"u8) || fourCc.SequenceEqual("ALPH"u8)) && extended)
            {
                // A colour profile and an alpha plane are image data, allowed only inside the extended format.
            }
            else
            {
                return ImageInspection.Refuse(ImageRefusal.Unsupported, "unexpected RIFF chunk", format);
            }

            position += 8 + (int)padded;
        }

        if (bitstreams != 1)
        {
            return ImageInspection.Refuse(ImageRefusal.Malformed, "not exactly one image bitstream", format);
        }

        if (width == 0 || height == 0)
        {
            return ImageInspection.Refuse(ImageRefusal.Malformed, "zero image dimension", format);
        }

        return new ImageInspection(format, width, height, ImageRefusal.None, null);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes) =>
        (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
}
