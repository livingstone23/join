using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Catalog.Products.Images.Queries;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Catalog.Products.Images.Queries.GetProductImages;

/// <summary>SPEC 70 — Dapper reads of the images: list with author, and the bytes for the inline content routes.</summary>
public sealed class GetProductImagesQueryHandlerTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly FakeSqlConnectionFactory _connection = new();

    public GetProductImagesQueryHandlerTests() => _user.SetupGet(x => x.CompanyId).Returns(() => _companyId);

    [Fact]
    public async Task List_ReturnsTheLiveImagesOfTheTenantProduct_WithTheAuthor()
    {
        var productId = Guid.NewGuid();
        _connection.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Value"] = 1 }),
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(), ["ProductId"] = productId, ["OriginalName"] = "a.png", ["NewName"] = "x.png",
                ["ContentType"] = "image/png", ["SizeBytes"] = 10L, ["Width"] = 3, ["Height"] = 2, ["SortOrder"] = 1,
                ["IsPrimary"] = true, ["UploadedByUserId"] = Guid.NewGuid(), ["UploadedByUserName"] = "Ana Pérez ",
                ["CreatedAt"] = DateTime.UtcNow
            }));

        var response = await new GetProductImagesQueryHandler(_connection, _user.Object).Handle(new GetProductImagesQuery(productId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        var image = response.Data!.Should().ContainSingle().Subject;
        image.UploadedByUserName.Should().Be("Ana Pérez");
        image.Width.Should().Be(3);
        _connection.LastCommandText.Should().Contain("i.CompanyId = @CompanyId").And.Contain("i.GcRecord = 0").And.Contain("ORDER BY i.SortOrder");
    }

    [Fact]
    public async Task List_ForAMissingProduct_RespondsProductNotFound_AndWithoutCompany_CompanyRequired()
    {
        _connection.SetResults(FakeResultSet.Empty("Value"));
        var handler = new GetProductImagesQueryHandler(_connection, _user.Object);

        (await handler.Handle(new GetProductImagesQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.ProductNotFound);

        _user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        (await handler.Handle(new GetProductImagesQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Content_StreamsFromTheProviderThatStoredIt_BindingEveryParameter()
    {
        var imageId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        _connection.SetResults(FakeResultSet.FromRows(FileRow(StorageProviderKind.Local)));
        var (resolver, provider) = Resolver();
        provider.Setup(x => x.OpenReadAsync("products/k.png", It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream([1, 2]));

        var response = await new GetProductImageContentQueryHandler(_connection, _user.Object, resolver.Object)
            .Handle(new GetProductImageContentQuery(productId, imageId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.ContentType.Should().Be("image/png");
        _connection.CapturedParameters["ImageId"].Should().Be(imageId);
        _connection.CapturedParameters["ProductId"].Should().Be(productId);
        _connection.CapturedParameters["CompanyId"].Should().Be(_companyId);
        _connection.LastCommandText.Should().Contain("@ImageId").And.Contain("@ProductId").And.Contain("@CompanyId");
    }

    [Fact]
    public async Task Content_Errors_NotFoundProviderMissingAndFileMissing()
    {
        var (resolver, provider) = Resolver();
        var handler = new GetProductImageContentQueryHandler(_connection, _user.Object, resolver.Object);

        _connection.SetResults(FakeResultSet.Empty("Path"));
        (await handler.Handle(new GetProductImageContentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.ProductImageNotFound);

        _connection.SetResults(FakeResultSet.FromRows(FileRow(StorageProviderKind.AzureBlob)));
        (await handler.Handle(new GetProductImageContentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.StorageProviderNotConfigured);

        _connection.SetResults(FakeResultSet.FromRows(FileRow(StorageProviderKind.Local)));
        provider.Setup(x => x.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Stream?)null);
        (await handler.Handle(new GetProductImageContentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.FileNotFoundInStorage);

        _user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        (await handler.Handle(new GetProductImageContentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }

    internal static Dictionary<string, object?> FileRow(StorageProviderKind kind) => new()
    {
        ["Path"] = "products/k.png", ["StorageProvider"] = (int)kind, ["ContentType"] = "image/png", ["OriginalName"] = "a.png"
    };

    internal static (Mock<IFileStorageResolver> Resolver, Mock<IFileStorageService> Provider) Resolver()
    {
        var provider = new Mock<IFileStorageService>();
        var resolver = new Mock<IFileStorageResolver>();
        resolver.Setup(x => x.Resolve(StorageProviderKind.Local)).Returns(provider.Object);
        resolver.Setup(x => x.Resolve(StorageProviderKind.AzureBlob)).Returns((IFileStorageService?)null);
        return (resolver, provider);
    }
}
