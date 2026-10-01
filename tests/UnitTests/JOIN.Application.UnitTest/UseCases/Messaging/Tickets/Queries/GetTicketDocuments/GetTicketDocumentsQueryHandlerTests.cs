using System.Text;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.Tickets.Queries.GetTicketDocuments;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.GetTicketDocuments;

/// <summary>
/// Unit tests for <see cref="GetTicketDocumentsQueryHandler"/>. Uses the
/// shared FakeDapperInfrastructure to capture SQL text + parameters without
/// needing a real database.
/// </summary>
public sealed class GetTicketDocumentsQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new GetDocsTestContext(companyId: Guid.Empty);
        var handler = context.CreateHandler();
        var query = new GetTicketDocumentsQuery(_fixture.Create<Guid>());

        var response = await handler.Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTicketHasNoDocuments_ShouldReturnEmptyList()
    {
        var companyId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var context = new GetDocsTestContext(companyId);

        context.Connection.SetResults(FakeResultSet.Empty(
            "Id", "TicketId", "TicketLogsId", "DocumentType",
            "OriginalName", "ContentType", "SizeBytes",
            "CreatedByUserId", "CreatedByUserName", "CreatedAt"));

        var response = await context.CreateHandler().Handle(new GetTicketDocumentsQuery(ticketId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeEmpty();

        context.Connection.CapturedParameters["TicketId"].Should().Be(ticketId);
        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
    }

    [Fact]
    public async Task Handle_WhenTicketHasDocuments_ShouldReturnAllActiveRows()
    {
        var companyId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var context = new GetDocsTestContext(companyId);

        context.Connection.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["TicketId"] = ticketId,
                ["TicketLogsId"] = Guid.NewGuid(),
                ["DocumentType"] = "Pdf",
                ["OriginalName"] = "report.pdf",
                ["ContentType"] = "application/pdf",
                ["SizeBytes"] = 1024L,
                ["CreatedByUserId"] = Guid.NewGuid(),
                ["CreatedByUserName"] = "Ana Torres",
                ["CreatedAt"] = DateTime.UtcNow
            }));

        var response = await context.CreateHandler().Handle(new GetTicketDocumentsQuery(ticketId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Count.Should().Be(1);
        response.Data.First().DocumentType.Should().Be("Pdf");
        response.Data.First().CreatedByUserName.Should().Be("Ana Torres");

        context.Connection.LastCommandText.Should().Contain("ORDER BY td.Created DESC");
        context.Connection.LastCommandText.Should().Contain("WHERE td.TicketId = @TicketId");
        context.Connection.LastCommandText.Should().Contain("AND td.GcRecord = 0");
        context.Connection.LastCommandText.Should().NotContain("UNIQUEIDENTIFIER", "a non-GUID CreatedBy such as \"System\" must not break the query");
    }

    private sealed class GetDocsTestContext
    {
        public GetDocsTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetTicketDocumentsQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}