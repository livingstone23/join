using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.UseCases.Finance;
using JOIN.Application.UseCases.Catalog.Products.Commands;
using JOIN.Domain.Catalog;
using JOIN.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;
using AttachmentSettings = JOIN.Domain.Catalog.ProductAttachmentSettings;

namespace JOIN.Application.UnitTest.UseCases.Catalog.Products;

/// <summary>
/// SPEC 70 test bed: the finance InMemory context (real <c>UnitOfWork</c> and Application DI) with a live product, the
/// company's attachment limits, the four seed document types and an in-memory storage provider.
/// </summary>
internal sealed class ProductAttachmentTestBed : IDisposable
{
    private ProductAttachmentTestBed(FinanceTestContext context) => Context = context;

    public FinanceTestContext Context { get; }
    public Guid CompanyId => Context.CompanyId;
    public Guid ProductId { get; private set; }
    public Guid TechSheetTypeId { get; private set; }
    public Dictionary<string, byte[]> Stored { get; } = [];
    public Mock<IFileStorageService> Storage { get; } = new();
    public Mock<IFileStorageResolver> Resolver { get; } = new();
    public IUnitOfWork UnitOfWork => Context.Get<IUnitOfWork>();
    public ICurrentUserService CurrentUser => Context.CurrentUser.Object;

    public static async Task<ProductAttachmentTestBed> CreateAsync(bool withSettings = true)
    {
        var bed = new ProductAttachmentTestBed(await FinanceTestContext.CreateAsync());
        await bed.SeedAsync(withSettings);
        return bed;
    }

    public async Task<AttachmentSettings> SettingsAsync() =>
        await Context.Db.ProductAttachmentSettings.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == CompanyId && s.GcRecord == 0);

    public async Task<List<ProductImage>> ImagesAsync()
    {
        Context.Db.ChangeTracker.Clear();
        return await Context.Db.ProductImages.IgnoreQueryFilters().Where(i => i.ProductId == ProductId).OrderBy(i => i.SortOrder).ToListAsync();
    }

    public async Task<List<ProductDocument>> DocumentsAsync()
    {
        Context.Db.ChangeTracker.Clear();
        return await Context.Db.ProductDocuments.IgnoreQueryFilters().Where(d => d.ProductId == ProductId).ToListAsync();
    }

    public async Task DeactivateProductAsync()
    {
        var product = await Context.Db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == ProductId);
        product.IsActive = false;
        await Context.Db.SaveChangesAsync();
        Context.Db.ChangeTracker.Clear();
    }

    private async Task SeedAsync(bool withSettings)
    {
        Storage.SetupGet(x => x.Kind).Returns(StorageProviderKind.Local);
        Storage.Setup(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream content, string key, string _, CancellationToken _) =>
            {
                using var copy = new MemoryStream();
                content.CopyTo(copy);
                Stored[key] = copy.ToArray();
                return new FileStorageSaveResult(key, copy.Length);
            });
        Resolver.SetupGet(x => x.Current).Returns(Storage.Object);
        Resolver.Setup(x => x.Resolve(StorageProviderKind.Local)).Returns(Storage.Object);

        var created = await Context.SendAsync(new CreateProductCommand { Code = "IMG-1", Name = "Producto con imágenes", Kind = (int)ProductKind.Product, ReferencePrice = 10m });
        ProductId = created.Data!.Id;

        if (withSettings)
        {
            Context.Db.ProductAttachmentSettings.Add(new AttachmentSettings
            {
                CompanyId = CompanyId,
                AllowedDocumentTypes = AttachmentSettings.DefaultAllowedDocumentTypes,
                MaxDocumentSizeBytes = AttachmentSettings.DefaultMaxDocumentSizeBytes,
                MaxDocumentsPerProduct = AttachmentSettings.DefaultMaxDocumentsPerProduct,
                AllowedImageFormats = AttachmentSettings.DefaultAllowedImageFormats,
                MaxImageSizeBytes = AttachmentSettings.DefaultMaxImageSizeBytes,
                MaxImagesPerProduct = AttachmentSettings.DefaultMaxImagesPerProduct
            });
        }

        var techSheet = new ProductDocumentType { CompanyId = CompanyId, Code = "TECH_SHEET", Name = "Ficha técnica", SortOrder = 1, IsActive = true };
        TechSheetTypeId = techSheet.Id;
        Context.Db.ProductDocumentTypes.Add(techSheet);
        Context.Db.ProductDocumentTypes.Add(new ProductDocumentType { CompanyId = CompanyId, Code = "OTHER", Name = "Otro", SortOrder = 4, IsActive = false });
        await Context.Db.SaveChangesAsync();
        Context.Db.ChangeTracker.Clear();
    }

    public void Dispose() => Context.Dispose();
}

/// <summary>Minimal valid headers of each accepted format (and a PDF to fake an image).</summary>
internal static class ProductImageSamples
{
    /// <summary>PNG 3×2: signature + IHDR.</summary>
    public static byte[] Png() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x02, 0x08, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    /// <summary>JPEG 3×2: SOI, a 16-byte APP0 segment, then SOF0.</summary>
    public static byte[] Jpeg() =>
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01,
        0x00, 0x01, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x02, 0x00, 0x03, 0x03, 0x01, 0x22, 0x00
    ];

    /// <summary>WEBP: "RIFF" size "WEBP" + the given chunk header (padded to 32 bytes).</summary>
    public static byte[] Webp(string chunk, params (int Offset, byte Value)[] bytes)
    {
        var content = new byte[32];
        "RIFF"u8.CopyTo(content);
        "WEBP"u8.CopyTo(content.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(content, 12);
        foreach (var (offset, value) in bytes)
        {
            content[offset] = value;
        }

        return content;
    }

    public static byte[] Pdf() => "%PDF-1.4 fake"u8.ToArray();
}
