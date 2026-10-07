using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetTicketUserCompanies;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies.Queries.GetTicketUserCompanies;

/// <summary>
/// Unit tests for <see cref="GetTicketUserCompaniesQueryHandler"/>: tenant isolation,
/// filter wiring, pagination sanitization and ordering.
/// </summary>
public sealed class GetTicketUserCompaniesQueryHandlerTests
{
    private readonly Fixture _fixture = new();
    private readonly PaginationSettings _pagination = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new GetTicketUserCompaniesQueryHandlerTestContext(Guid.Empty, _pagination);
        var handler = context.CreateHandler();

        var response = await handler.Handle(new GetTicketUserCompaniesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNoFilters_ShouldQueryWithoutExtraClauses()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new GetTicketUserCompaniesQueryHandlerTestContext(companyId, _pagination);
        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "CompanyId", "CompanyName", "UserId", "UserName", "UserEmail", "IsSuperAdminTicket", "CanFinishTicket", "CanResolveTicket", "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetTicketUserCompaniesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.TotalCount.Should().Be(0);
        context.Connection.LastCommandText.Should().Contain("WHERE tuc.CompanyId = @TenantId AND tuc.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("ORDER BY tuc.Created DESC");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");
    }

    [Fact]
    public async Task Handle_WhenFiltersProvided_ShouldAppendThemToWhereClause()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new GetTicketUserCompaniesQueryHandlerTestContext(companyId, _pagination);
        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "CompanyId", "CompanyName", "UserId", "UserName", "UserEmail", "IsSuperAdminTicket", "CanFinishTicket", "CanResolveTicket", "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(
            new GetTicketUserCompaniesQuery
            {
                UserId = userId,
                IsSuperAdminTicket = true,
                CanFinishTicket = true,
                CanResolveTicket = false
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var sql = context.Connection.LastCommandText;
        sql.Should().Contain("AND tuc.UserId = @UserId");
        sql.Should().Contain("AND tuc.IsSuperAdminTicket = @IsSuperAdminTicket");
        sql.Should().Contain("AND tuc.CanFinishTicket = @CanFinishTicket");
        sql.Should().Contain("AND tuc.CanResolveTicket = @CanResolveTicket");
        context.Connection.CapturedParameters["UserId"].Should().Be(userId);
        context.Connection.CapturedParameters["IsSuperAdminTicket"].Should().Be(true);
    }

    [Fact]
    public async Task Handle_WhenPageSizeExceedsMax_ShouldBeSanitizedByPaginationSettings()
    {
        var companyId = _fixture.Create<Guid>();
        var pagination = new PaginationSettings { DefaultPageSize = 5, MaxPageSize = 10, MinPageSize = 1 };
        var context = new GetTicketUserCompaniesQueryHandlerTestContext(companyId, pagination);
        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "CompanyId", "CompanyName", "UserId", "UserName", "UserEmail", "IsSuperAdminTicket", "CanFinishTicket", "CanResolveTicket", "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(
            new GetTicketUserCompaniesQuery { PageNumber = 99, PageSize = 500 },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.PageSize.Should().Be(10);
        response.Data.PageNumber.Should().Be(99);
        context.Connection.CapturedParameters["PageSize"].Should().Be(10);
    }

    [Fact]
    public async Task Handle_WhenResultsReturned_ShouldPopulatePagedResult()
    {
        var companyId = _fixture.Create<Guid>();
        var rowId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new GetTicketUserCompaniesQueryHandlerTestContext(companyId, _pagination);
        context.Connection.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = rowId,
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN",
                ["UserId"] = userId,
                ["UserName"] = "Manager Test",
                ["UserEmail"] = "manager@join.com",
                ["IsSuperAdminTicket"] = true,
                ["CanFinishTicket"] = true,
                ["CanResolveTicket"] = true,
                ["CreatedAt"] = DateTime.UtcNow
            }),
            FakeResultSet.FromScalar(1));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetTicketUserCompaniesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.TotalCount.Should().Be(1);
        response.Data.Items.Should().HaveCount(1);
        response.Data.Items.ElementAt(0).Id.Should().Be(rowId);
        response.Data.TotalPages.Should().Be(1);
    }

    private sealed class GetTicketUserCompaniesQueryHandlerTestContext
    {
        public GetTicketUserCompaniesQueryHandlerTestContext(Guid companyId, PaginationSettings pagination)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
            PaginationOptions = Options.Create(pagination);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();
        public IOptions<PaginationSettings> PaginationOptions { get; }

        public GetTicketUserCompaniesQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object, PaginationOptions);
    }
}
