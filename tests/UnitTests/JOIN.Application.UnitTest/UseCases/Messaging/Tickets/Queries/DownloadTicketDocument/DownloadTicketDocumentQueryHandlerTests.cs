using System.Text;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.Tickets.Queries.DownloadTicketDocument;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.DownloadTicketDocument;

/// <summary>
/// Unit tests for <see cref="DownloadTicketDocumentQueryHandler"/>.
/// </summary>
/// <remarks>
/// SPEC 49-post: la query lee la columna <c>StorageProvider</c> de la fila
/// y delega al adapter que guardó el archivo (no al provider activo
/// del entorno). El handler traduce <c>null</c> del resolver a
/// <c>STORAGE_PROVIDER_NOT_CONFIGURED</c> (503).
/// </remarks>
public sealed class DownloadTicketDocumentQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new DownloadTestContext(companyId: Guid.Empty);
        var handler = context.CreateHandler();
        var query = new DownloadTicketDocumentQuery(_fixture.Create<Guid>(), _fixture.Create<Guid>());

        var response = await handler.Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMetadataNotFound_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new DownloadTestContext(companyId);
        var handler = context.CreateHandler();
        var query = new DownloadTicketDocumentQuery(_fixture.Create<Guid>(), _fixture.Create<Guid>());

        context.Connection.SetResults(FakeResultSet.Empty(
            "Id", "TicketId", "CompanyId", "Path", "StorageProvider", "ContentType", "OriginalName"));

        var response = await handler.Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_DOCUMENT_NOT_FOUND");
        context.FileStorageServiceMock.Verify(x => x.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMetadataExistsButFileMissingInStorage_ShouldReturnFileNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var docId = _fixture.Create<Guid>();
        var context = new DownloadTestContext(companyId);

        context.Connection.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = docId,
                ["TicketId"] = ticketId,
                ["CompanyId"] = companyId,
                ["Path"] = $"{companyId}/{ticketId}/missing.bin",
                ["StorageProvider"] = StorageProviderKind.Local,
                ["ContentType"] = "application/pdf",
                ["OriginalName"] = "missing.pdf"
            }));
        context.FileStorageServiceMock
            .Setup(x => x.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        var query = new DownloadTicketDocumentQuery(ticketId, docId);
        var response = await context.CreateHandler().Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("FILE_NOT_FOUND_IN_STORAGE");
    }

    [Fact]
    public async Task Handle_WhenMetadataAndFileBothExist_ShouldReturnDownloadResult()
    {
        var companyId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var docId = _fixture.Create<Guid>();
        var context = new DownloadTestContext(companyId);

        var path = $"{companyId}/{ticketId}/present.pdf";
        context.Connection.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = docId,
                ["TicketId"] = ticketId,
                ["CompanyId"] = companyId,
                ["Path"] = path,
                ["StorageProvider"] = StorageProviderKind.Local,
                ["ContentType"] = "application/pdf",
                ["OriginalName"] = "present.pdf"
            }));

        var stream = new MemoryStream(Encoding.UTF8.GetBytes("PDF content"));
        context.FileStorageServiceMock
            .Setup(x => x.OpenReadAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream);

        var query = new DownloadTicketDocumentQuery(ticketId, docId);
        var response = await context.CreateHandler().Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Content.Should().BeSameAs(stream);
        response.Data.ContentType.Should().Be("application/pdf");
        response.Data.OriginalName.Should().Be("present.pdf");
    }

    /// <summary>
    /// SPEC 49-post: la fila pide un provider que el entorno actual no
    /// tiene configurado (por ejemplo, los <c>CloudflareR2</c> cuando
    /// solo está registrada la sección <c>Local</c>). El handler
    /// devuelve <c>STORAGE_PROVIDER_NOT_CONFIGURED</c> (503) sin
    /// intentar abrir ningún adapter.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRowProviderIsNotConfiguredInThisEnvironment_ShouldReturnStorageProviderNotConfiguredError()
    {
        var companyId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var docId = _fixture.Create<Guid>();
        var context = new DownloadTestContext(companyId);

        context.Connection.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = docId,
                ["TicketId"] = ticketId,
                ["CompanyId"] = companyId,
                ["Path"] = $"{companyId}/{ticketId}/legacy.bin",
                ["StorageProvider"] = StorageProviderKind.CloudflareR2,
                ["ContentType"] = "application/pdf",
                ["OriginalName"] = "legacy.pdf"
            }));

        // Resolver devuelve null para CloudflareR2 (sección no presente
        // en este entorno) y un adapter para Local.
        context.FileStorageResolverMock
            .Setup(x => x.Resolve(StorageProviderKind.CloudflareR2))
            .Returns((IFileStorageService?)null);
        context.FileStorageResolverMock
            .Setup(x => x.Resolve(StorageProviderKind.Local))
            .Returns(context.FileStorageServiceMock.Object);

        var query = new DownloadTicketDocumentQuery(ticketId, docId);
        var response = await context.CreateHandler().Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("STORAGE_PROVIDER_NOT_CONFIGURED");
        context.FileStorageServiceMock.Verify(x => x.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// SPEC 49-post: la descarga abre el archivo con el provider de la
    /// fila, no con el provider activo del entorno. Una fila Local
    /// con provider activo S3 sigue leyendo desde Local.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRowProviderDiffersFromCurrentProvider_ShouldOpenFileWithTheRowProvider()
    {
        var companyId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var docId = _fixture.Create<Guid>();
        var context = new DownloadTestContext(companyId);

        var path = $"{companyId}/{ticketId}/legacy.pdf";
        context.Connection.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = docId,
                ["TicketId"] = ticketId,
                ["CompanyId"] = companyId,
                ["Path"] = path,
                ["StorageProvider"] = StorageProviderKind.Local,
                ["ContentType"] = "application/pdf",
                ["OriginalName"] = "legacy.pdf"
            }));

        // Adapter "local" — el de la fila.
        var localAdapter = new Mock<IFileStorageService>();
        localAdapter.SetupGet(x => x.Kind).Returns(StorageProviderKind.Local);
        var legacyStream = new MemoryStream(Encoding.UTF8.GetBytes("legacy"));
        localAdapter.Setup(x => x.OpenReadAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(legacyStream);

        // Resolver: Local → localAdapter; S3 (provider activo) → mock por defecto.
        context.FileStorageResolverMock
            .Setup(x => x.Resolve(StorageProviderKind.Local))
            .Returns(localAdapter.Object);
        context.FileStorageResolverMock
            .Setup(x => x.Current)
            .Returns(context.FileStorageServiceMock.Object);

        var query = new DownloadTicketDocumentQuery(ticketId, docId);
        var response = await context.CreateHandler().Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Content.Should().BeSameAs(legacyStream);
        localAdapter.Verify(x => x.OpenReadAsync(path, It.IsAny<CancellationToken>()), Times.Once);
        context.FileStorageServiceMock.Verify(x => x.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class DownloadTestContext
    {
        public DownloadTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);

            // Default: el resolver devuelve el mismo mock del storage
            // service para todos los providers. Los tests que quieran
            // simular un provider ausente usan Setup sobre un kind
            // específico.
            FileStorageServiceMock.SetupGet(x => x.Kind).Returns(StorageProviderKind.Local);
            FileStorageResolverMock.Setup(x => x.Resolve(It.IsAny<StorageProviderKind>()))
                .Returns(FileStorageServiceMock.Object);
            FileStorageResolverMock.Setup(x => x.Current)
                .Returns(FileStorageServiceMock.Object);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IFileStorageService> FileStorageServiceMock { get; } = new();
        public Mock<IFileStorageResolver> FileStorageResolverMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public DownloadTicketDocumentQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object, FileStorageResolverMock.Object);
    }
}
