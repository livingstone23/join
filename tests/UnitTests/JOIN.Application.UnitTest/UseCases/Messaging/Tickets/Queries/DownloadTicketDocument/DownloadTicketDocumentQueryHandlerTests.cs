using System.Text;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.Tickets.Queries.DownloadTicketDocument;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.DownloadTicketDocument;

/// <summary>
/// Unit tests for <see cref="DownloadTicketDocumentQueryHandler"/>.
/// </summary>
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
            "Id", "TicketId", "CompanyId", "Path", "ContentType", "OriginalName"));

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

    private sealed class DownloadTestContext
    {
        public DownloadTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IFileStorageService> FileStorageServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public DownloadTicketDocumentQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object, FileStorageServiceMock.Object);
    }
}