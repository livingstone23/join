using FluentAssertions;
using JOIN.Application.UseCases.Catalog.Products.Images.Commands;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.UseCases.Catalog.Products.Images.Commands.ManageProductImages;

/// <summary>SPEC 70 — format by extension and by signature, and the pixel size read from each header.</summary>
public sealed class ProductImageInspectorTests
{
    [Theory]
    [InlineData(".jpg", ProductImageFormat.Jpeg)]
    [InlineData(".jpeg", ProductImageFormat.Jpeg)]
    [InlineData(".png", ProductImageFormat.Png)]
    [InlineData(".webp", ProductImageFormat.Webp)]
    public void FromExtension_KnownExtensions(string extension, ProductImageFormat expected) =>
        ProductImageInspector.FromExtension(extension).Should().Be(expected);

    [Theory]
    [InlineData(".gif")]
    [InlineData(".pdf")]
    [InlineData("")]
    public void FromExtension_UnknownExtensions_AreNull(string extension) =>
        ProductImageInspector.FromExtension(extension).Should().BeNull();

    [Fact]
    public void FromSignature_RecognizesEachFormat_AndNothingElse()
    {
        ProductImageInspector.FromSignature(ProductImageSamples.Png()).Should().Be(ProductImageFormat.Png);
        ProductImageInspector.FromSignature(ProductImageSamples.Jpeg()).Should().Be(ProductImageFormat.Jpeg);
        ProductImageInspector.FromSignature(ProductImageSamples.Webp("VP8 ")).Should().Be(ProductImageFormat.Webp);
        ProductImageInspector.FromSignature(ProductImageSamples.Pdf()).Should().BeNull();
        ProductImageInspector.FromSignature(new byte[64]).Should().BeNull("zeros never fall back to the extension");
        ProductImageInspector.FromSignature([]).Should().BeNull();
    }

    [Fact]
    public void ReadDimensions_Png_And_Jpeg()
    {
        ProductImageInspector.ReadDimensions(ProductImageFormat.Png, ProductImageSamples.Png()).Should().Be(((int?)3, (int?)2));
        ProductImageInspector.ReadDimensions(ProductImageFormat.Jpeg, ProductImageSamples.Jpeg()).Should().Be(((int?)3, (int?)2));
    }

    [Fact]
    public void ReadDimensions_Webp_LossyLosslessAndExtended()
    {
        // VP8: 14-bit width/height at 26 and 28.
        ProductImageInspector.ReadDimensions(ProductImageFormat.Webp, ProductImageSamples.Webp("VP8 ", (26, 7), (28, 9)))
            .Should().Be(((int?)7, (int?)9));
        // VP8L: 0x2F, then (width - 1) | (height - 1) << 14 → 4 × 5.
        ProductImageInspector.ReadDimensions(ProductImageFormat.Webp, ProductImageSamples.Webp("VP8L", (20, 0x2F), (21, 0x03), (23, 0x01)))
            .Should().Be(((int?)4, (int?)5));
        // VP8X: 24-bit (width - 1) at 24 and (height - 1) at 27 → 300 × 200.
        ProductImageInspector.ReadDimensions(ProductImageFormat.Webp, ProductImageSamples.Webp("VP8X", (24, 43), (25, 1), (27, 199)))
            .Should().Be(((int?)300, (int?)200));
    }

    [Fact]
    public void ReadDimensions_TruncatedOrUnusualHeaders_AreNull()
    {
        ProductImageInspector.ReadDimensions(ProductImageFormat.Png, ProductImageSamples.Png()[..16]).Should().Be(((int?)null, (int?)null));
        ProductImageInspector.ReadDimensions(ProductImageFormat.Jpeg, [0xFF, 0xD8, 0xFF, 0xD9, 0, 0, 0, 0, 0, 0, 0, 0]).Should().Be(((int?)null, (int?)null));
        ProductImageInspector.ReadDimensions(ProductImageFormat.Jpeg, [0xFF, 0xD8, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0]).Should().Be(((int?)null, (int?)null));
        ProductImageInspector.ReadDimensions(ProductImageFormat.Webp, ProductImageSamples.Webp("VP8L")).Should().Be(((int?)null, (int?)null));
        ProductImageInspector.ReadDimensions(ProductImageFormat.Webp, ProductImageSamples.Webp("ALPH")).Should().Be(((int?)null, (int?)null));
        ProductImageInspector.ReadDimensions(ProductImageFormat.Webp, ProductImageSamples.Webp("VP8X")[..20]).Should().Be(((int?)null, (int?)null));
        ProductImageInspector.ReadDimensions(ProductImageFormat.None, ProductImageSamples.Png()).Should().Be(((int?)null, (int?)null));
    }

    [Fact]
    public void ReadDimensions_Jpeg_SkipsFillBytesAndRestartMarkers()
    {
        byte[] content =
        [
            0xFF, 0xD8, 0xFF, 0xFF, 0xFF, 0xD0, 0xFF, 0x01, 0xFF, 0xC2, 0x00, 0x11, 0x08, 0x00, 0x05, 0x00, 0x06, 0x03
        ];

        ProductImageInspector.ReadDimensions(ProductImageFormat.Jpeg, content).Should().Be(((int?)6, (int?)5));
    }
}
