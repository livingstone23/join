using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Catalog.ProductImages;
using JOIN.Application.UseCases.Catalog.ProductAttachmentSettings;
using JOIN.Application.UseCases.Catalog.ProductDocumentTypes;
using JOIN.Domain.Catalog;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;
using Moq;
using AttachmentSettingsEntity = JOIN.Domain.Catalog.ProductAttachmentSettings;
using ProductEntity = JOIN.Domain.Catalog.Product;
using ProductImageEntity = JOIN.Domain.Catalog.ProductImage;
using ProductDocumentTypeEntity = JOIN.Domain.Catalog.ProductDocumentType;

namespace JOIN.Application.UnitTest.UseCases.Catalog.ProductImages;

/// <summary>
/// SPEC 70 — coverage of the per-tenant product attachment settings (rule 5/7): one live row per company,
/// CRUD through the same envelope as <c>LeadAttachmentSettings</c>.
/// </summary>
public sealed class Spec70ProductAttachmentSettingsHandlerTests
{
    private static readonly Fixture _fixture = new();

    [Fact]
    public async Task Get_WhenRowMissing_RespondsNotConfigured()
    {
        var context = new SettingsContext(Guid.NewGuid());
        var response = await context.GetHandler().Handle(new GetProductAttachmentSettingsQuery(), CancellationToken.None);
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CatalogErrorCodes.ProductAttachmentSettingsNotConfigured);
    }

    [Fact]
    public async Task Get_WhenRowExists_ReturnsDto()
    {
        var companyId = Guid.NewGuid();
        var connection = new FakeSqlConnectionFactory();
        connection.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(),
            ["CompanyId"] = companyId,
            ["AllowedDocumentTypes"] = (int)AttachmentSettingsEntity.DefaultAllowedDocumentTypes,
            ["MaxDocumentSizeBytes"] = AttachmentSettingsEntity.DefaultMaxDocumentSizeBytes,
            ["MaxDocumentsPerProduct"] = AttachmentSettingsEntity.DefaultMaxDocumentsPerProduct,
            ["AllowedImageFormats"] = (int)AttachmentSettingsEntity.DefaultAllowedImageFormats,
            ["MaxImageSizeBytes"] = AttachmentSettingsEntity.DefaultMaxImageSizeBytes,
            ["MaxImagesPerProduct"] = AttachmentSettingsEntity.DefaultMaxImagesPerProduct,
            ["CreatedAt"] = DateTime.UtcNow
        }));
        var context = new SettingsContext(companyId) { GetConnectionFactory = connection };

        var response = await context.GetHandler().Handle(new GetProductAttachmentSettingsQuery(), CancellationToken.None);
        response.IsSuccess.Should().BeTrue();
        response.Data!.MaxDocumentsPerProduct.Should().Be(AttachmentSettingsEntity.DefaultMaxDocumentsPerProduct);
    }

    [Fact]
    public async Task Post_WhenRowAlreadyExists_RespondsAlreadyExists()
    {
        var companyId = Guid.NewGuid();
        var context = new SettingsContext(companyId);
        context.SettingsRepositoryMock.SetupRows(new[] { new AttachmentSettingsEntity { CompanyId = companyId } });

        var response = await context.CreateHandler().Handle(
            new CreateProductAttachmentSettingsCommand { AllowedDocumentTypes = (int)DocumentType.Pdf, MaxDocumentSizeBytes = 1024, MaxDocumentsPerProduct = 1, AllowedImageFormats = (int)ProductImageFormat.Jpeg, MaxImageSizeBytes = 1024, MaxImagesPerProduct = 1 },
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CatalogErrorCodes.ProductAttachmentSettingsAlreadyExists);
    }

    [Fact]
    public async Task Post_WhenRowMissing_Inserts()
    {
        var companyId = Guid.NewGuid();
        var context = new SettingsContext(companyId);
        context.SettingsRepositoryMock.SetupRows(Array.Empty<AttachmentSettingsEntity>());

        var response = await context.CreateHandler().Handle(
            new CreateProductAttachmentSettingsCommand
            {
                AllowedDocumentTypes = (int)DocumentType.Pdf | (int)DocumentType.Word,
                MaxDocumentSizeBytes = 4_000_000,
                MaxDocumentsPerProduct = 10,
                AllowedImageFormats = (int)ProductImageFormat.Jpeg | (int)ProductImageFormat.Png,
                MaxImageSizeBytes = 2_000_000,
                MaxImagesPerProduct = 3
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.MaxDocumentsPerProduct.Should().Be(10);
        response.Data.MaxImagesPerProduct.Should().Be(3);
        context.SettingsRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<AttachmentSettingsEntity>()), Times.Once);
    }

    [Fact]
    public async Task Put_WhenRowMissing_RespondsNotConfigured()
    {
        var context = new SettingsContext(Guid.NewGuid());
        context.SettingsRepositoryMock.SetupRows(Array.Empty<AttachmentSettingsEntity>());

        var response = await context.UpdateHandler().Handle(
            new UpdateProductAttachmentSettingsCommand(),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CatalogErrorCodes.ProductAttachmentSettingsNotConfigured);
    }

    private sealed class SettingsContext
    {
        public SettingsContext(Guid companyId)
        {
            CompanyId = companyId;
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            UnitOfWorkMock.Setup(x => x.GetRepository<AttachmentSettingsEntity>()).Returns(SettingsRepositoryMock.Object);
        }

        public Guid CompanyId { get; }
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<AttachmentSettingsEntity>> SettingsRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeSqlConnectionFactory GetConnectionFactory { get; set; } = new();

        public GetProductAttachmentSettingsQueryHandler GetHandler() =>
            new(GetConnectionFactory, CurrentUserServiceMock.Object);

        public CreateProductAttachmentSettingsCommandHandler CreateHandler() =>
            new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);

        public UpdateProductAttachmentSettingsCommandHandler UpdateHandler() =>
            new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}

/// <summary>SPEC 70 — image upload walks the rule-7 limits: settings missing, format not allowed, signature mismatch, too large, per-product limit.</summary>
public sealed class Spec70ProductImageUploadHandlerTests
{
    [Fact]
    public async Task Upload_WhenSettingsMissing_RespondsNotConfigured()
    {
        var context = new ProductContext();
        context.AttachmentRepositoryMock.SetupRows(Array.Empty<AttachmentSettingsEntity>());
        var (resolver, _) = NewStorage();
        var handler = new UploadProductImageCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            resolver,
            context.Primary);

        var response = await handler.Handle(new UploadProductImageCommand(context.ProductId, "ok.png", "image/png", PngBytes), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CatalogErrorCodes.ProductAttachmentSettingsNotConfigured);
    }

    [Fact]
    public async Task Upload_WhenProductIsInactive_RespondsNotActive()
    {
        var context = new ProductContext(isActive: false);
        context.AttachmentRepositoryMock.SetupRows(new[] { DefaultSettings(context) });
        var (resolver, _) = NewStorage();
        var handler = new UploadProductImageCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            resolver,
            context.Primary);

        var response = await handler.Handle(new UploadProductImageCommand(context.ProductId, "ok.png", "image/png", PngBytes), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CatalogErrorCodes.ProductNotActive);
    }

    [Fact]
    public async Task Upload_WhenExtensionIsGifAndSignatureIsJpeg_RespondsFormatNotAllowed()
    {
        var context = new ProductContext();
        context.AttachmentRepositoryMock.SetupRows(new[] { DefaultSettings(context) });
        var (resolver, _) = NewStorage();
        var handler = new UploadProductImageCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            resolver,
            context.Primary);

        var response = await handler.Handle(new UploadProductImageCommand(context.ProductId, "ok.gif", "image/gif", JpegBytes), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CatalogErrorCodes.ProductImageFormatNotAllowed);
    }

    [Fact]
    public async Task Upload_WhenFileExceedsLimit_RespondsTooLarge()
    {
        var context = new ProductContext();
        var settings = DefaultSettings(context); settings.MaxImageSizeBytes = 16;
        context.AttachmentRepositoryMock.SetupRows(new[] { settings });
        var (resolver, _) = NewStorage();
        var handler = new UploadProductImageCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            resolver,
            context.Primary);

        var response = await handler.Handle(new UploadProductImageCommand(context.ProductId, "ok.png", "image/png", new byte[64]), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CatalogErrorCodes.ProductImageTooLarge);
    }

    [Fact]
    public async Task Upload_WhenAtMaxImages_RespondsImageLimit()
    {
        var context = new ProductContext();
        var settings = DefaultSettings(context); settings.MaxImagesPerProduct = 1;
        context.AttachmentRepositoryMock.SetupRows(new[] { settings });
        var existing = new ProductImageEntity
        {
            CompanyId = context.CompanyId,
            ProductId = context.ProductId,
            OriginalName = "existing.png",
            NewName = "existing.png",
            Path = "products/existing",
            ContentType = "image/png",
            SizeBytes = 10,
            SortOrder = 1,
            IsPrimary = true,
            UploadedByUserId = context.ActorUserId
        };
        context.ProductImageRepositoryMock.SetupRows(new[] { existing });
        var (resolver, _) = NewStorage();
        var handler = new UploadProductImageCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            resolver,
            context.Primary);

        var response = await handler.Handle(new UploadProductImageCommand(context.ProductId, "ok.png", "image/png", PngBytes), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CatalogErrorCodes.ProductImageLimit);
    }

    [Fact]
    public async Task Upload_WhenAllChecksPass_PersistsAndMarksPrimary()
    {
        var context = new ProductContext();
        context.AttachmentRepositoryMock.SetupRows(new[] { DefaultSettings(context) });
        context.ProductImageRepositoryMock.SetupRows(Array.Empty<ProductImageEntity>());
        context.TrackWrites();
        var (resolver, _) = NewStorage();
        var handler = new UploadProductImageCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            resolver,
            context.Primary);

        var response = await handler.Handle(new UploadProductImageCommand(context.ProductId, "ok.png", "image/png", PngBytes), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var inserted = context.CapturedImageInserts.Should().ContainSingle().Subject;
        inserted.IsPrimary.Should().BeTrue();
        inserted.SortOrder.Should().Be(1);
        inserted.Path.Should().StartWith($"products/{context.CompanyId}/{context.ProductId}/images/");
    }

    // ---- helpers ----

    private static readonly byte[] PngBytes = new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4
    };

    private static readonly byte[] JpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };

    private static (IFileStorageResolver Resolver, Mock<IFileStorageService> CurrentMock) NewStorage()
    {
        var current = new Mock<IFileStorageService>();
        current.SetupGet(x => x.Kind).Returns(StorageProviderKind.Local);
        current.Setup(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string key, string _, CancellationToken _) => new FileStorageSaveResult(key, 1024));
        var resolverMock = new Mock<IFileStorageResolver>();
        resolverMock.SetupGet(x => x.Current).Returns(current.Object);
        resolverMock.Setup(x => x.Resolve(It.IsAny<StorageProviderKind>())).Returns(current.Object);
        return (resolverMock.Object, current);
    }

    private static AttachmentSettingsEntity DefaultSettings(ProductContext context) => new()
    {
        CompanyId = context.CompanyId,
        AllowedDocumentTypes = DocumentType.Pdf | DocumentType.Word | DocumentType.Excel | DocumentType.Image,
        MaxDocumentSizeBytes = 10L * 1024 * 1024,
        MaxDocumentsPerProduct = 20,
        AllowedImageFormats = ProductImageFormat.Jpeg | ProductImageFormat.Png | ProductImageFormat.Webp,
        MaxImageSizeBytes = 5L * 1024 * 1024,
        MaxImagesPerProduct = 5
    };

    private sealed class ProductContext
    {
        public ProductContext(bool isActive = true)
        {
            CompanyId = Guid.NewGuid();
            ActorUserId = Guid.NewGuid();
            ProductId = Guid.NewGuid();
            Product = new ProductEntity
            {
                CompanyId = CompanyId,
                Code = "ABC",
                Name = "Sample",
                ReferencePrice = 1m,
                IsActive = isActive
            };
            EntityIdSetter.SetId(Product, ProductId);

            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(ActorUserId.ToString());

            UnitOfWorkMock.Setup(x => x.GetRepository<ProductEntity>()).Returns(ProductRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<AttachmentSettingsEntity>()).Returns(AttachmentRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ProductImageEntity>()).Returns(ProductImageRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ProductDocumentTypeEntity>()).Returns(ProductDocumentTypeRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationUser>()).Returns(ApplicationUserRepositoryMock.Object);

            ProductRepositoryMock.Setup(x => x.GetAsync(ProductId)).ReturnsAsync(Product);
            Primary = new ProductImagePrimaryCoordinator(UnitOfWorkMock.Object);
        }

        public Guid CompanyId { get; }
        public Guid ActorUserId { get; }
        public Guid ProductId { get; }
        public ProductEntity Product { get; }
        public ProductImagePrimaryCoordinator Primary { get; }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<ProductEntity>> ProductRepositoryMock { get; } = new();
        public Mock<IGenericRepository<AttachmentSettingsEntity>> AttachmentRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ProductImageEntity>> ProductImageRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ProductDocumentTypeEntity>> ProductDocumentTypeRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> ApplicationUserRepositoryMock { get; } = new();

        public List<ProductImageEntity> CapturedImageInserts { get; } = new();
        public List<ProductImageEntity> CapturedImageUpdates { get; } = new();

        public void TrackWrites()
        {
            ProductImageRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<ProductImageEntity>()))
                .Callback<ProductImageEntity>(e => { EntityIdSetter.SetId(e, Guid.NewGuid()); CapturedImageInserts.Add(e); })
                .ReturnsAsync(true);
            ProductImageRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<ProductImageEntity>()))
                .Callback<ProductImageEntity>(e => CapturedImageUpdates.Add(e))
                .ReturnsAsync(true);
        }
    }
}

/// <summary>SPEC 70 — the image content query (used by both the Products and Leads thumbnail endpoints).</summary>
public sealed class GetProductImageContentQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenRowMissing_RespondsNotFound()
    {
        var context = new SettingsContext(Guid.NewGuid());
        var resolver = new Mock<IFileStorageResolver>();
        var handler = new GetProductImageContentQueryHandler(new FakeSqlConnectionFactory(), context.CurrentUserServiceMock.Object, resolver.Object);

        var response = await handler.Handle(new GetProductImageContentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CatalogErrorCodes.ProductImageNotFound);
    }
}

internal sealed class SettingsContext
{
    public SettingsContext(Guid companyId)
    {
        CompanyId = companyId;
        CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
    }

    public Guid CompanyId { get; }
    public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
}