using FluentAssertions;
using JOIN.Application.UseCases.Catalog.Products.Commands;
using JOIN.Domain.Catalog;

namespace JOIN.Application.UnitTest.UseCases.Catalog.Products.Common;

/// <summary>
/// SPEC 70 rule 8 — deleting a product deletes its images and documents with the same stamp; restoring it brings
/// back those rows (and only those), keeping the primary flag. Files are never touched.
/// </summary>
public sealed class ProductAttachmentCascadeCoordinatorTests
{
    [Fact]
    public async Task DeleteAndRestoreProduct_CascadesToImagesAndDocuments()
    {
        using var bed = await ProductAttachmentTestBed.CreateAsync();
        var image = new ProductImage
        {
            CompanyId = bed.CompanyId, ProductId = bed.ProductId, OriginalName = "a.png", NewName = "a.png", Path = "p/a.png",
            ContentType = "image/png", SortOrder = 1, IsPrimary = true
        };
        var document = new ProductDocument
        {
            CompanyId = bed.CompanyId, ProductId = bed.ProductId, ProductDocumentTypeId = bed.TechSheetTypeId,
            DisplayName = "Ficha", OriginalName = "f.pdf", NewName = "f.pdf", Path = "p/f.pdf", ContentType = "application/pdf"
        };
        // Deleted before the product, on another day: the product restore must not bring it back.
        var oldDocument = new ProductDocument
        {
            CompanyId = bed.CompanyId, ProductId = bed.ProductId, ProductDocumentTypeId = bed.TechSheetTypeId,
            DisplayName = "Vieja", OriginalName = "v.pdf", NewName = "v.pdf", Path = "p/v.pdf", ContentType = "application/pdf",
            GcRecord = 20200101
        };
        bed.Context.Db.AddRange(image, document, oldDocument);
        await bed.Context.Db.SaveChangesAsync();
        bed.Context.Db.ChangeTracker.Clear();

        (await bed.Context.SendAsync(new DeleteProductCommand(bed.ProductId))).IsSuccess.Should().BeTrue();

        var stamp = (await bed.ImagesAsync()).Single().GcRecord;
        stamp.Should().NotBe(0);
        (await bed.DocumentsAsync()).Single(d => d.Id == document.Id).GcRecord.Should().Be(stamp);

        bed.Context.IsSuperAdmin = true;
        (await bed.Context.SendAsync(new RestoreProductCommand(bed.ProductId))).IsSuccess.Should().BeTrue();

        var restoredImage = (await bed.ImagesAsync()).Single();
        restoredImage.GcRecord.Should().Be(0);
        restoredImage.IsPrimary.Should().BeTrue();
        var documents = await bed.DocumentsAsync();
        documents.Single(d => d.Id == document.Id).GcRecord.Should().Be(0);
        documents.Single(d => d.Id == oldDocument.Id).GcRecord.Should().Be(20200101);
        bed.Storage.Invocations.Should().NotContain(i => i.Method.Name == "DeleteAsync");
    }
}
