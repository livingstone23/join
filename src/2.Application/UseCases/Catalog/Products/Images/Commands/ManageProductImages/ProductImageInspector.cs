using JOIN.Domain.Enums;

namespace JOIN.Application.UseCases.Catalog.Products.Images.Commands;

/// <summary>
/// SPEC 70 — reads what the server needs from an uploaded image without an imaging library: the format from
/// the extension, the format from the signature (first bytes) and, when the header allows it, the size in pixels.
/// </summary>
public static class ProductImageInspector
{
    /// <summary>The format the extension claims, or <c>null</c> for an extension the catalog does not accept.</summary>
    public static ProductImageFormat? FromExtension(string extension) => extension switch
    {
        ".jpg" or ".jpeg" => ProductImageFormat.Jpeg,
        ".png" => ProductImageFormat.Png,
        ".webp" => ProductImageFormat.Webp,
        _ => null
    };

    /// <summary>The format the bytes really are, or <c>null</c> when no known signature matches.</summary>
    public static ProductImageFormat? FromSignature(ReadOnlySpan<byte> content)
    {
        // JPEG: FF D8 FF
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return ProductImageFormat.Jpeg;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (content.Length >= 8 && content[..8].SequenceEqual(PngSignature))
        {
            return ProductImageFormat.Png;
        }

        // WEBP: "RIFF" <size> "WEBP"
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8))
        {
            return ProductImageFormat.Webp;
        }

        return null;
    }

    /// <summary>Width and height in pixels, or <c>(null, null)</c> when the header is truncated or unusual.</summary>
    public static (int? Width, int? Height) ReadDimensions(ProductImageFormat format, ReadOnlySpan<byte> content) => format switch
    {
        ProductImageFormat.Png => ReadPng(content),
        ProductImageFormat.Jpeg => ReadJpeg(content),
        ProductImageFormat.Webp => ReadWebp(content),
        _ => (null, null)
    };

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // IHDR is always the first chunk: width at 16, height at 20 (big endian).
    private static (int?, int?) ReadPng(ReadOnlySpan<byte> c) =>
        c.Length >= 24 && c[12..16].SequenceEqual("IHDR"u8)
            ? (BigEndian32(c[16..]), BigEndian32(c[20..]))
            : (null, null);

    // Walks the segments until a start-of-frame marker (SOF0..SOF15, except DHT C4, JPG C8 and DAC CC).
    private static (int?, int?) ReadJpeg(ReadOnlySpan<byte> c)
    {
        var position = 2;
        while (position + 9 < c.Length)
        {
            if (c[position] != 0xFF)
            {
                return (null, null);
            }

            var marker = c[position + 1];
            if (marker == 0xFF)
            {
                position++;
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                position += 2;
                continue;
            }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                var height = (c[position + 5] << 8) | c[position + 6];
                var width = (c[position + 7] << 8) | c[position + 8];
                return width > 0 && height > 0 ? (width, height) : (null, null);
            }

            if (marker is 0xD9 or 0xDA)
            {
                return (null, null);
            }

            var length = (c[position + 2] << 8) | c[position + 3];
            if (length < 2)
            {
                return (null, null);
            }

            position += 2 + length;
        }

        return (null, null);
    }

    private static (int?, int?) ReadWebp(ReadOnlySpan<byte> c)
    {
        if (c.Length < 30)
        {
            return (null, null);
        }

        var chunk = c[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            // Lossy: 14-bit width and height (little endian) after the 3-byte frame tag and the 3-byte start code.
            return ((c[26] | (c[27] << 8)) & 0x3FFF, (c[28] | (c[29] << 8)) & 0x3FFF);
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            // Lossless: signature 0x2F at 20, then 14 bits width - 1 and 14 bits height - 1.
            if (c[20] != 0x2F)
            {
                return (null, null);
            }

            var width = 1 + (((c[22] & 0x3F) << 8) | c[21]);
            var height = 1 + (((c[24] & 0x0F) << 10) | (c[23] << 2) | ((c[22] & 0xC0) >> 6));
            return (width, height);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            // Extended: 24-bit canvas width - 1 at 24 and height - 1 at 27 (little endian).
            return (1 + (c[24] | (c[25] << 8) | (c[26] << 16)), 1 + (c[27] | (c[28] << 8) | (c[29] << 16)));
        }

        return (null, null);
    }

    private static int? BigEndian32(ReadOnlySpan<byte> c)
    {
        var value = (c[0] << 24) | (c[1] << 16) | (c[2] << 8) | c[3];
        return value > 0 ? value : null;
    }
}
