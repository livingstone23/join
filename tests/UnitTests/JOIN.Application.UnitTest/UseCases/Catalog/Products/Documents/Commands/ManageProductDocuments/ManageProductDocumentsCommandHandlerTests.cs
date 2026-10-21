using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UseCases.Catalog.Products.Documents.Commands;
using JOIN.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Application.UnitTest.UseCases.Catalog.Products.Documents.Commands.ManageProductDocuments;

/// <summary>SPEC 70 — documents of a product: upload checks in order (B.10), delete and SuperAdmin restore.</summary>
public sealed class ManageProductDocumentsCommandHandlerTests
{
    private static Task<Response<ProductDocumentDto>> UploadAsync(
        ProductAttachmentTestBed bed, string name = "ficha.pdf", byte[]? content = null, Guid? typeId = null, Guid? productId = null) =>
        new UploadProductDocumentCommandHandler(bed.UnitOfWork, bed.CurrentUser, bed.Resolver.Object).Handle(
            new UploadProductDocumentCommand(productId ?? bed.ProductId, typeId ?? bed.TechSheetTypeId, "  Ficha técnica  ", "  ", name, "application/pdf", content ?? [1, 2, 3]),
            CancellationToken.None);

    private static async Task SetLimitsAsync(ProductAttachmentTestBed bed, long? maxSize = null, int? maxCount = null)
    {
        var settings = await bed.Context.Db.ProductAttachmentSettings.SingleAsync(s => s.CompanyId == bed.CompanyId);
        settings.MaxDocumentSizeBytes = maxSize ?? settings.MaxDocumentSizeBytes;
        settings.MaxDocumentsPerProduct = maxCount ?? settings.MaxDocumentsPerProduct;
        await bed.Context.Db.SaveChangesAsync();
        bed.Context.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Upload_StoresTheDocument_WithItsTypeTrimmedNamesAndPath()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();

        var response = await UploadAsync(bed);

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.DisplayName.Should().Be("Ficha técnica");
        response.Data.Description.Should().BeNull();
        response.Data.ProductDocumentTypeName.Should().Be("Ficha técnica");
        response.Data.DocumentType.Should().Be((int)DocumentType.Pdf);
        var document = (await bed.DocumentsAsync()).Single();
        document.Path.Should().StartWith($"products/{bed.CompanyId}/{bed.ProductId}/documents/");
        bed.Stored.Should().ContainKey(document.Path);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("notas.txt")] // Text is a known type but not in the default allowed types
    public async Task Upload_FileTypeNotAllowed(string name)
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();

        (await UploadAsync(bed, name)).Message.Should().Be(CatalogErrorCodes.ProductDocumentFileTypeNotAllowed);
    }

    [Fact]
    public async Task Upload_TooLargeAndOverTheLimit()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        await SetLimitsAsync(bed, maxSize: 2, maxCount: 1);

        (await UploadAsync(bed)).Message.Should().Be(CatalogErrorCodes.ProductDocumentTooLarge);
        (await UploadAsync(bed, content: [1])).IsSuccess.Should().BeTrue();
        (await UploadAsync(bed, content: [1])).Message.Should().Be(CatalogErrorCodes.ProductDocumentLimit);
    }

    [Fact]
    public async Task Upload_ChecksTenantProductTypeAndSettingsFirst()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync(withSettings: false);
        var inactiveType = await bed.Context.Db.ProductDocumentTypes.SingleAsync(t => t.Code == "OTHER");

        (await UploadAsync(bed, productId: Guid.NewGuid())).Message.Should().Be(CatalogErrorCodes.ProductNotFound);
        (await UploadAsync(bed, typeId: Guid.NewGuid())).Message.Should().Be(CatalogErrorCodes.ProductDocumentTypeNotFound);
        (await UploadAsync(bed, typeId: inactiveType.Id)).Message.Should().Be(CatalogErrorCodes.ProductDocumentTypeNotFound);
        (await UploadAsync(bed)).Message.Should().Be(CatalogErrorCodes.ProductAttachmentSettingsNotConfigured);

        await bed.DeactivateProductAsync();
        (await UploadAsync(bed)).Message.Should().Be(CatalogErrorCodes.ProductNotActive);
    }

    [Fact]
    public async Task Delete_KeepsTheFile_AndRestoreIsSuperAdminOnly()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        var document = (await UploadAsync(bed)).Data!;

        (await bed.Context.SendAsync(new DeleteProductDocumentCommand(Guid.NewGuid(), document.Id))).Message.Should().Be(CatalogErrorCodes.ProductNotFound);
        (await bed.Context.SendAsync(new DeleteProductDocumentCommand(bed.ProductId, Guid.NewGuid()))).Message.Should().Be(CatalogErrorCodes.ProductDocumentNotFound);
        (await bed.Context.SendAsync(new DeleteProductDocumentCommand(bed.ProductId, document.Id))).IsSuccess.Should().BeTrue();
        (await bed.DocumentsAsync()).Single().GcRecord.Should().NotBe(0);
        bed.Stored.Should().HaveCount(1, "the physical file is kept");

        (await bed.Context.SendAsync(new RestoreProductDocumentCommand(bed.ProductId, document.Id))).Message.Should().Be(CatalogErrorCodes.SuperAdminRequired);
        bed.Context.IsSuperAdmin = true;
        (await bed.Context.SendAsync(new RestoreProductDocumentCommand(Guid.NewGuid(), document.Id))).Message.Should().Be(CatalogErrorCodes.ProductDocumentNotFound);
        (await bed.Context.SendAsync(new RestoreProductDocumentCommand(bed.ProductId, document.Id))).IsSuccess.Should().BeTrue();
        (await bed.DocumentsAsync()).Single().GcRecord.Should().Be(0);
        (await bed.Context.SendAsync(new RestoreProductDocumentCommand(bed.ProductId, document.Id))).Message.Should().Be("NOT_DELETED");
    }

    [Fact]
    public async Task Restore_AtTheLimit_RespondsDocumentLimit()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        await SetLimitsAsync(bed, maxCount: 1);
        var first = (await UploadAsync(bed)).Data!;
        await bed.Context.SendAsync(new DeleteProductDocumentCommand(bed.ProductId, first.Id));
        await UploadAsync(bed);
        bed.Context.IsSuperAdmin = true;

        (await bed.Context.SendAsync(new RestoreProductDocumentCommand(bed.ProductId, first.Id))).Message.Should().Be(CatalogErrorCodes.ProductDocumentLimit);

        bed.Context.ActingCompanyId = Guid.Empty;
        (await bed.Context.SendAsync(new RestoreProductDocumentCommand(bed.ProductId, first.Id))).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
        (await bed.Context.SendAsync(new DeleteProductDocumentCommand(bed.ProductId, first.Id))).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }
}
