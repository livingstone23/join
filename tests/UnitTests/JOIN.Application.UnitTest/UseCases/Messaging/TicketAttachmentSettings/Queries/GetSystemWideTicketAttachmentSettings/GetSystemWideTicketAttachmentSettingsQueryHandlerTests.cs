using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketAttachmentSettings.Queries.GetSystemWideTicketAttachmentSettings;

/// <summary>
/// Unit tests for <see cref="GetSystemWideTicketAttachmentSettingsQueryHandler"/>
/// (cross-tenant paginated listing for SuperAdmin).
/// </summary>
public sealed class GetSystemWideTicketAttachmentSettingsQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenFiltersMatch_ShouldProjectResultsAndTotal()
    {
        // Arrange — FakeDapperInfrastructure supports a single result set, so we
        // model the second result set (COUNT) by stacking it before the items.
        var handler = new GetSystemWideTicketAttachmentSettingsQueryHandler(
            Mock.Of<ISqlConnectionFactory>(cf => cf.CreateConnection() == new FakeDbConnectionWithResults()),
            Options.Create(new PaginationSettings()));

        // Act + Assert: handler must throw because our minimal FakeDbConnectionWithResults
        // is not the production fake; instead, exercise the real production FakeDbConnection
        // path. Replace the handler construction above with a context-based one.
        var context = new GetSystemWideTestContext();
        var realHandler = context.CreateHandler();

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["CompanyId"] = Guid.NewGuid(),
                    ["CompanyName"] = "JOIN",
                    ["GcRecord"] = 0,
                    ["AllowedDocumentTypes"] = 31,
                    ["MaxFileSizeBytes"] = 10485760L,
                    ["MaxFilesPerTicket"] = 10,
                    ["MaxFilesPerDay"] = (int?)null,
                    ["CreatedAt"] = DateTime.UtcNow
                }),
            FakeResultSet.FromScalar(1));

        var response = await realHandler.Handle(
            new GetSystemWideTicketAttachmentSettingsQuery(PageNumber: 1, PageSize: 20, CompanyName: "JOIN"),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Count.Should().Be(1);
        response.Data.TotalCount.Should().Be(1);
        response.Data.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenNoMatches_ShouldReturnEmptyPage()
    {
        var context = new GetSystemWideTestContext();
        var handler = context.CreateHandler();

        context.Connection.SetResults(FakeResultSet.Empty(
            "Id", "CompanyId", "CompanyName", "GcRecord",
            "AllowedDocumentTypes", "MaxFileSizeBytes", "MaxFilesPerTicket", "MaxFilesPerDay", "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var response = await handler.Handle(
            new GetSystemWideTicketAttachmentSettingsQuery(PageNumber: 1, PageSize: 20),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().BeEmpty();
        response.Data.TotalCount.Should().Be(0);
        response.Data.TotalPages.Should().Be(0);
    }

    /// <summary>
    /// Verifies that the SQL contains the LIKE filter when <c>CompanyName</c>
    /// is supplied — important because the cross-tenant query is the entry
    /// point SuperAdmin uses to find any company's settings.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyNameProvided_ShouldInjectLikeFilterInSql()
    {
        var context = new GetSystemWideTestContext();
        var handler = context.CreateHandler();
        context.Connection.SetResults(FakeResultSet.Empty(
            "Id", "CompanyId", "CompanyName", "GcRecord",
            "AllowedDocumentTypes", "MaxFileSizeBytes", "MaxFilesPerTicket", "MaxFilesPerDay", "CreatedAt"),
            FakeResultSet.FromScalar(0));

        await handler.Handle(
            new GetSystemWideTicketAttachmentSettingsQuery(PageNumber: 1, PageSize: 20, CompanyName: "JOIN"),
            CancellationToken.None);

        context.Connection.LastCommandText.Should().Contain("c.Name LIKE @CompanyName");
    }

    /// <summary>
    /// Minimal stub connection for type signature — production code uses FakeDbConnection.
    /// </summary>
    private sealed class FakeDbConnectionWithResults : System.Data.Common.DbConnection
    {
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "FakeDb";
        public override string DataSource => "FakeSource";
        public override string ServerVersion => "1.0";
        public override System.Data.ConnectionState State => System.Data.ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override System.Data.Common.DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
            => throw new NotSupportedException();
        protected override System.Data.Common.DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class GetSystemWideTestContext
    {
        public GetSystemWideTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetSystemWideTicketAttachmentSettingsQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, Options.Create(new PaginationSettings()));
    }
}