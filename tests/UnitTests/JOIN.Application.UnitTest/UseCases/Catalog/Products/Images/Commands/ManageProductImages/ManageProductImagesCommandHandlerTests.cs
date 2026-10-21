using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UseCases.Catalog.Products.Images.Commands;
using JOIN.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Catalog.Products.Images.Commands.ManageProductImages;

/// <summary>
/// SPEC 70 — images of a product: the upload checks in order (B.7), one primary per product
/// (<see cref="ProductImagePrimaryCoordinator"/>), reorder, delete and SuperAdmin restore.
/// </summary>
public sealed class ManageProductImagesCommandHandlerTests
{
    private static UploadProductImageCommandHandler UploadHandler(ProductAttachmentTestBed bed) =>
        new(bed.UnitOfWork, bed.CurrentUser, bed.Resolver.Object, bed.Context.Get<ProductImagePrimaryCoordinator>());

    private static Task<Response<ProductImageDto>> UploadAsync(ProductAttachmentTestBed bed, string name = "foto.png", byte[]? content = null, Guid? productId = null) =>
        UploadHandler(bed).Handle(
            new UploadProductImageCommand(productId ?? bed.ProductId, name, "application/octet-stream", content ?? ProductImageSamples.Png()),
            CancellationToken.None);

    [Fact]
    public async Task Upload_FirstImage_IsPrimary_WithServerContentTypeDimensionsAndPath()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();

        var response = await UploadAsync(bed);

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.IsPrimary.Should().BeTrue();
        response.Data.SortOrder.Should().Be(1);
        response.Data.ContentType.Should().Be("image/png", "the server fixes it from the signature, not from the client");
        response.Data.Width.Should().Be(3);
        response.Data.Height.Should().Be(2);
        var image = (await bed.ImagesAsync()).Single();
        image.Path.Should().StartWith($"products/{bed.CompanyId}/{bed.ProductId}/images/");
        bed.Stored.Should().ContainKey(image.Path);
    }

    [Fact]
    public async Task Upload_SecondImage_IsNotPrimary_AndGoesLast()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        await UploadAsync(bed);

        var second = await UploadAsync(bed, "foto.jpg", ProductImageSamples.Jpeg());

        second.Data!.IsPrimary.Should().BeFalse();
        second.Data.SortOrder.Should().Be(2);
        second.Data.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task Upload_SixthImage_RespondsImageLimit()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        for (var i = 0; i < 5; i++)
        {
            (await UploadAsync(bed)).IsSuccess.Should().BeTrue();
        }

        (await UploadAsync(bed)).Message.Should().Be(CatalogErrorCodes.ProductImageLimit);
    }

    [Fact]
    public async Task Upload_HonorsTheCompanyLimitBelowFive()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        var settings = await bed.Context.Db.ProductAttachmentSettings.SingleAsync(s => s.CompanyId == bed.CompanyId);
        settings.MaxImagesPerProduct = 1;
        await bed.Context.Db.SaveChangesAsync();
        bed.Context.Db.ChangeTracker.Clear();
        await UploadAsync(bed);

        (await UploadAsync(bed)).Message.Should().Be(CatalogErrorCodes.ProductImageLimit);
    }

    [Theory]
    [InlineData("foto.jpg")] // a PDF renamed to .jpg
    [InlineData("foto.png")]
    public async Task Upload_PdfContentWithAnImageExtension_RespondsFormatNotAllowed(string name)
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();

        (await UploadAsync(bed, name, ProductImageSamples.Pdf())).Message.Should().Be(CatalogErrorCodes.ProductImageFormatNotAllowed);
    }

    [Fact]
    public async Task Upload_ExtensionThatDoesNotMatchTheSignature_RespondsFormatNotAllowed()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();

        (await UploadAsync(bed, "foto.jpg", ProductImageSamples.Png())).Message.Should().Be(CatalogErrorCodes.ProductImageFormatNotAllowed);
        (await UploadAsync(bed, "foto.gif", ProductImageSamples.Jpeg())).Message.Should().Be(CatalogErrorCodes.ProductImageFormatNotAllowed);
    }

    [Fact]
    public async Task Upload_FormatOutsideTheCompanyFormats_RespondsFormatNotAllowed()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        var settings = await bed.Context.Db.ProductAttachmentSettings.SingleAsync(s => s.CompanyId == bed.CompanyId);
        settings.AllowedImageFormats = ProductImageFormat.Jpeg;
        await bed.Context.Db.SaveChangesAsync();
        bed.Context.Db.ChangeTracker.Clear();

        (await UploadAsync(bed, "foto.webp", ProductImageSamples.Webp("VP8X"))).Message.Should().Be(CatalogErrorCodes.ProductImageFormatNotAllowed);
        (await UploadAsync(bed, "foto.jpg", ProductImageSamples.Jpeg())).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Upload_TooLarge_RespondsTooLarge()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        var settings = await bed.Context.Db.ProductAttachmentSettings.SingleAsync(s => s.CompanyId == bed.CompanyId);
        settings.MaxImageSizeBytes = 16;
        await bed.Context.Db.SaveChangesAsync();
        bed.Context.Db.ChangeTracker.Clear();

        (await UploadAsync(bed)).Message.Should().Be(CatalogErrorCodes.ProductImageTooLarge);
    }

    [Fact]
    public async Task Upload_ChecksTenantProductStateAndSettingsFirst()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync(withSettings: false);

        (await UploadAsync(bed, productId: Guid.NewGuid())).Message.Should().Be(CatalogErrorCodes.ProductNotFound);
        (await UploadAsync(bed)).Message.Should().Be(CatalogErrorCodes.ProductAttachmentSettingsNotConfigured);

        await bed.DeactivateProductAsync();
        (await UploadAsync(bed)).Message.Should().Be(CatalogErrorCodes.ProductNotActive);

        bed.Context.ActingCompanyId = Guid.Empty;
        (await UploadAsync(bed)).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task SetPrimary_LeavesASinglePrimary()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        var first = (await UploadAsync(bed)).Data!;
        var second = (await UploadAsync(bed)).Data!;

        var response = await bed.Context.SendAsync(new SetPrimaryProductImageCommand(bed.ProductId, second.Id));

        response.IsSuccess.Should().BeTrue(response.Message);
        var images = await bed.ImagesAsync();
        images.Single(i => i.IsPrimary).Id.Should().Be(second.Id);
        images.Single(i => i.Id == first.Id).IsPrimary.Should().BeFalse();
        (await bed.Context.SendAsync(new SetPrimaryProductImageCommand(bed.ProductId, second.Id))).IsSuccess.Should().BeTrue("marking the primary again is a no-op");
        (await bed.Context.SendAsync(new SetPrimaryProductImageCommand(bed.ProductId, Guid.NewGuid()))).Message.Should().Be(CatalogErrorCodes.ProductImageNotFound);
    }

    [Fact]
    public async Task DeletePrimary_PromotesTheNextBySortOrder_AndKeepsTheFile()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        var first = (await UploadAsync(bed)).Data!;
        var second = (await UploadAsync(bed)).Data!;
        var third = (await UploadAsync(bed)).Data!;
        await bed.Context.SendAsync(new ReorderProductImagesCommand { ProductId = bed.ProductId, ImageIds = [first.Id, third.Id, second.Id] });

        var response = await bed.Context.SendAsync(new DeleteProductImageCommand(bed.ProductId, first.Id));

        response.IsSuccess.Should().BeTrue(response.Message);
        var images = await bed.ImagesAsync();
        images.Single(i => i.Id == first.Id).GcRecord.Should().NotBe(0);
        images.Where(i => i.GcRecord == 0).Single(i => i.IsPrimary).Id.Should().Be(third.Id);
        bed.Storage.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        (await bed.Context.SendAsync(new DeleteProductImageCommand(bed.ProductId, first.Id))).Message.Should().Be(CatalogErrorCodes.ProductImageNotFound);
    }

    [Fact]
    public async Task DeleteNonPrimary_KeepsThePrimary()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        var first = (await UploadAsync(bed)).Data!;
        var second = (await UploadAsync(bed)).Data!;

        (await bed.Context.SendAsync(new DeleteProductImageCommand(bed.ProductId, second.Id))).IsSuccess.Should().BeTrue();

        (await bed.ImagesAsync()).Where(i => i.GcRecord == 0).Single(i => i.IsPrimary).Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task Reorder_RequiresExactlyTheLiveImages()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        var first = (await UploadAsync(bed)).Data!;
        var second = (await UploadAsync(bed)).Data!;

        (await bed.Context.SendAsync(new ReorderProductImagesCommand { ProductId = bed.ProductId, ImageIds = [first.Id] }))
            .Message.Should().Be(CatalogErrorCodes.InvalidProductImageOrder);
        (await bed.Context.SendAsync(new ReorderProductImagesCommand { ProductId = bed.ProductId, ImageIds = [first.Id, Guid.NewGuid()] }))
            .Message.Should().Be(CatalogErrorCodes.InvalidProductImageOrder);

        var response = await bed.Context.SendAsync(new ReorderProductImagesCommand { ProductId = bed.ProductId, ImageIds = [second.Id, first.Id] });

        response.IsSuccess.Should().BeTrue(response.Message);
        (await bed.ImagesAsync()).Select(i => i.Id).Should().Equal(second.Id, first.Id);
    }

    [Fact]
    public async Task Restore_IsSuperAdminOnly_AndDoesNotStealThePrimary()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        var first = (await UploadAsync(bed)).Data!;
        var second = (await UploadAsync(bed)).Data!;
        await bed.Context.SendAsync(new DeleteProductImageCommand(bed.ProductId, first.Id));

        (await bed.Context.SendAsync(new RestoreProductImageCommand(bed.ProductId, first.Id))).Message.Should().Be(CatalogErrorCodes.SuperAdminRequired);

        bed.Context.IsSuperAdmin = true;
        var response = await bed.Context.SendAsync(new RestoreProductImageCommand(bed.ProductId, first.Id));

        response.IsSuccess.Should().BeTrue(response.Message);
        var images = await bed.ImagesAsync();
        images.Should().OnlyContain(i => i.GcRecord == 0);
        images.Single(i => i.IsPrimary).Id.Should().Be(second.Id, "the restored image was primary but another one holds the flag now");
        images.Single(i => i.Id == first.Id).SortOrder.Should().Be(3, "a restored image goes after the live ones (SortOrder 2)");
    }

    [Fact]
    public async Task Restore_WhenNoLiveImageIsPrimary_RestoredImageBecomesPrimary()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        var first = (await UploadAsync(bed)).Data!;
        var second = (await UploadAsync(bed)).Data!;
        await bed.Context.SendAsync(new SetPrimaryProductImageCommand(bed.ProductId, second.Id));
        await bed.Context.SendAsync(new DeleteProductImageCommand(bed.ProductId, first.Id));
        await bed.Context.SendAsync(new DeleteProductImageCommand(bed.ProductId, second.Id));
        bed.Context.IsSuperAdmin = true;

        (await bed.Context.SendAsync(new RestoreProductImageCommand(bed.ProductId, first.Id))).IsSuccess.Should().BeTrue();

        var restored = (await bed.ImagesAsync()).Single(i => i.GcRecord == 0);
        restored.Id.Should().Be(first.Id);
        restored.IsPrimary.Should().BeTrue("it was not primary when deleted, but no live image holds the flag");
        restored.SortOrder.Should().Be(1);
    }

    [Fact]
    public async Task Restore_AtTheLimit_RespondsImageLimit_AndForeignOrUnknownImagesAreNotFound()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        var settings = await bed.Context.Db.ProductAttachmentSettings.SingleAsync(s => s.CompanyId == bed.CompanyId);
        settings.MaxImagesPerProduct = 1;
        await bed.Context.Db.SaveChangesAsync();
        bed.Context.Db.ChangeTracker.Clear();
        var first = (await UploadAsync(bed)).Data!;
        await bed.Context.SendAsync(new DeleteProductImageCommand(bed.ProductId, first.Id));
        await UploadAsync(bed);
        bed.Context.IsSuperAdmin = true;

        (await bed.Context.SendAsync(new RestoreProductImageCommand(bed.ProductId, first.Id))).Message.Should().Be(CatalogErrorCodes.ProductImageLimit);
        (await bed.Context.SendAsync(new RestoreProductImageCommand(Guid.NewGuid(), first.Id))).Message.Should().Be(CatalogErrorCodes.ProductImageNotFound);
        (await bed.Context.SendAsync(new RestoreProductImageCommand(bed.ProductId, Guid.NewGuid()))).Message.Should().Be(CatalogErrorCodes.ProductImageNotFound);

        bed.Context.ActingCompanyId = Guid.Empty;
        (await bed.Context.SendAsync(new RestoreProductImageCommand(bed.ProductId, first.Id))).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Restore_WhileTheProductIsDeleted_RespondsParentDeleted()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        var first = (await UploadAsync(bed)).Data!;
        await bed.Context.SendAsync(new DeleteProductImageCommand(bed.ProductId, first.Id));
        await bed.Context.SendAsync(new JOIN.Application.UseCases.Catalog.Products.Commands.DeleteProductCommand(bed.ProductId));
        bed.Context.IsSuperAdmin = true;

        (await bed.Context.SendAsync(new RestoreProductImageCommand(bed.ProductId, first.Id))).Message.Should().Be("PARENT_DELETED");
    }
}
