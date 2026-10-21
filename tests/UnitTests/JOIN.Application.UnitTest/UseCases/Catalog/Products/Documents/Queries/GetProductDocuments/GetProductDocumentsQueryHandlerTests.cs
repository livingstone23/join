using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Catalog.Products.Images.Queries.GetProductImages;
using JOIN.Application.UseCases.Catalog.Products.Documents.Queries;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Catalog.Products.Documents.Queries.GetProductDocuments;

/// <summary>SPEC 70 — Dapper reads of the documents: list with type and author, and the download.</summary>
public sealed class GetProductDocumentsQueryHandlerTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly FakeSqlConnectionFactory _connection = new();

    public GetProductDocumentsQueryHandlerTests() => _user.SetupGet(x => x.CompanyId).Returns(() => _companyId);

    [Fact]
    public async Task List_ReturnsTheLiveDocuments_WithTypeAndAuthor()
    {
        _connection.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Value"] = 1 }),
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(), ["ProductId"] = Guid.NewGuid(), ["ProductDocumentTypeId"] = Guid.NewGuid(),
                ["ProductDocumentTypeName"] = "Ficha técnica", ["DisplayName"] = "Ficha", ["Description"] = null,
                ["DocumentType"] = (int)DocumentType.Pdf, ["OriginalName"] = "f.pdf", ["NewName"] = "x.pdf",
                ["ContentType"] = "application/pdf", ["SizeBytes"] = 3L, ["UploadedByUserId"] = Guid.NewGuid(),
                ["UploadedByUserName"] = " ", ["CreatedAt"] = DateTime.UtcNow
            }));

        var response = await new GetProductDocumentsQueryHandler(_connection, _user.Object).Handle(new GetProductDocumentsQuery(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        var document = response.Data!.Should().ContainSingle().Subject;
        document.ProductDocumentTypeName.Should().Be("Ficha técnica");
        document.UploadedByUserName.Should().BeEmpty();
        _connection.LastCommandText.Should().Contain("d.CompanyId = @CompanyId").And.Contain("d.GcRecord = 0");
    }

    [Fact]
    public async Task List_Errors()
    {
        var handler = new GetProductDocumentsQueryHandler(_connection, _user.Object);
        _connection.SetResults(FakeResultSet.Empty("Value"));

        (await handler.Handle(new GetProductDocumentsQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.ProductNotFound);

        _user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        (await handler.Handle(new GetProductDocumentsQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Download_StreamsTheFile_BindingEveryParameter()
    {
        var documentId = Guid.NewGuid();
        _connection.SetResults(FakeResultSet.FromRows(GetProductImagesQueryHandlerTests.FileRow(StorageProviderKind.Local)));
        var (resolver, provider) = GetProductImagesQueryHandlerTests.Resolver();
        provider.Setup(x => x.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream([1]));

        var response = await new DownloadProductDocumentQueryHandler(_connection, _user.Object, resolver.Object)
            .Handle(new DownloadProductDocumentQuery(Guid.NewGuid(), documentId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.OriginalName.Should().Be("a.png");
        _connection.CapturedParameters["DocumentId"].Should().Be(documentId);
        _connection.LastCommandText.Should().Contain("@DocumentId");
    }

    [Fact]
    public async Task Download_Errors()
    {
        var (resolver, provider) = GetProductImagesQueryHandlerTests.Resolver();
        var handler = new DownloadProductDocumentQueryHandler(_connection, _user.Object, resolver.Object);

        _connection.SetResults(FakeResultSet.Empty("Path"));
        (await handler.Handle(new DownloadProductDocumentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.ProductDocumentNotFound);

        _connection.SetResults(FakeResultSet.FromRows(GetProductImagesQueryHandlerTests.FileRow(StorageProviderKind.AzureBlob)));
        (await handler.Handle(new DownloadProductDocumentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.StorageProviderNotConfigured);

        _connection.SetResults(FakeResultSet.FromRows(GetProductImagesQueryHandlerTests.FileRow(StorageProviderKind.Local)));
        provider.Setup(x => x.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Stream?)null);
        (await handler.Handle(new DownloadProductDocumentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.FileNotFoundInStorage);

        _user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        (await handler.Handle(new DownloadProductDocumentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(CatalogErrorCodes.CompanyRequired);
    }
}
