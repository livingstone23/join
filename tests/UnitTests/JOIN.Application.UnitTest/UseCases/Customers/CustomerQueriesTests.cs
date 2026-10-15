using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Customers.CustomerStages.Queries;
using JOIN.Application.UseCases.Customers.Customers.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Customers;

/// <summary>
/// Dapper read side of SPEC 55: stage catalog and status history, always against the <c>customer</c> schema.
/// </summary>
public sealed class CustomerQueriesTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static readonly string[] StageColumns =
        ["Id", "CompanyId", "Code", "Name", "Description", "SortOrder", "IsDefault", "IsActive", "CreatedAt", "CustomersCount"];

    private static readonly string[] HistoryColumns =
        ["Id", "CustomerId", "Action", "Source", "SourceReferenceId", "Notes", "UserId", "UserEmail", "OccurredAt"];

    private sealed class Context
    {
        public Context(Guid companyId, bool isSuperAdmin = false)
        {
            CurrentUser.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUser.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            Factory.Setup(x => x.CreateConnection()).Returns(() => Connection);
        }

        public Mock<ICurrentUserService> CurrentUser { get; } = new();
        public Mock<ISqlConnectionFactory> Factory { get; } = new();
        public SequencedFakeDbConnection Connection { get; } = new();
        public IOptions<PaginationSettings> Pagination { get; } =
            Options.Create(new PaginationSettings { DefaultPageNumber = 1, DefaultPageSize = 10, MaxPageSize = 50 });
    }

    private static Dictionary<string, object?> StageRow(string code) => new()
    {
        ["Id"] = Guid.NewGuid(),
        ["CompanyId"] = CompanyId,
        ["Code"] = code,
        ["Name"] = code,
        ["Description"] = null,
        ["SortOrder"] = 1,
        ["IsDefault"] = code == "CUSTOMER",
        ["IsActive"] = true,
        ["CreatedAt"] = DateTime.UtcNow,
        ["CustomersCount"] = 2
    };

    [Fact]
    public async Task GetStages_ShouldListActiveLiveStagesOfTheCompanyBySortOrder()
    {
        var ctx = new Context(CompanyId);
        ctx.Connection.Then(FakeResultSet.FromRows(StageRow("LEAD"), StageRow("CUSTOMER")));

        var response = await new GetCustomerStagesQueryHandler(ctx.Factory.Object, ctx.CurrentUser.Object)
            .Handle(new GetCustomerStagesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().HaveCount(2);
        response.Data![1].CustomersCount.Should().Be(2);
        ctx.Connection.LastCommandText.Should()
            .Contain("FROM customer.customerstages s")
            .And.Contain("AND s.GcRecord = 0")
            .And.Contain("AND s.IsActive = TRUE")
            .And.Contain("ORDER BY s.SortOrder ASC, s.Name ASC");
    }

    [Fact]
    public async Task GetStages_WithInactiveAndDeletedForSuperAdmin_ShouldDropBothFilters()
    {
        var ctx = new Context(CompanyId, isSuperAdmin: true);
        ctx.Connection.Then(FakeResultSet.Empty(StageColumns));

        await new GetCustomerStagesQueryHandler(ctx.Factory.Object, ctx.CurrentUser.Object)
            .Handle(new GetCustomerStagesQuery(IncludeInactive: true, IncludeDeleted: true), CancellationToken.None);

        ctx.Connection.LastCommandText.Should().NotContain("s.GcRecord = 0").And.NotContain("s.IsActive = TRUE");
    }

    [Fact]
    public async Task GetStageById_ShouldReturnTheStageOrNotFound()
    {
        var ctx = new Context(CompanyId);
        ctx.Connection.Then(FakeResultSet.FromRows(StageRow("LEAD"))).Then(FakeResultSet.Empty(StageColumns));
        var handler = new GetCustomerStageByIdQueryHandler(ctx.Factory.Object, ctx.CurrentUser.Object);

        (await handler.Handle(new GetCustomerStageByIdQuery(Guid.NewGuid()), CancellationToken.None)).Data!.Code.Should().Be("LEAD");
        (await handler.Handle(new GetCustomerStageByIdQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be("CUSTOMER_STAGE_NOT_FOUND");
    }

    [Fact]
    public async Task StageQueries_WithoutCompany_ShouldAnswerCompanyRequired()
    {
        var ctx = new Context(Guid.Empty);

        (await new GetCustomerStagesQueryHandler(ctx.Factory.Object, ctx.CurrentUser.Object)
            .Handle(new GetCustomerStagesQuery(), CancellationToken.None)).Message.Should().Be("COMPANY_REQUIRED");
        (await new GetCustomerStageByIdQueryHandler(ctx.Factory.Object, ctx.CurrentUser.Object)
            .Handle(new GetCustomerStageByIdQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be("COMPANY_REQUIRED");
        (await new GetCustomerStatusHistoryQueryHandler(ctx.Factory.Object, ctx.CurrentUser.Object, ctx.Pagination)
            .Handle(new GetCustomerStatusHistoryQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task GetStatusHistory_ShouldListEventsNewestFirstWithNames()
    {
        var ctx = new Context(CompanyId);
        ctx.Connection.SetScalarResults(true);
        ctx.Connection
            .Then()
            .Then(
                FakeResultSet.FromRows(new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["CustomerId"] = Guid.NewGuid(),
                    ["Action"] = 4,
                    ["Source"] = 4,
                    ["SourceReferenceId"] = Guid.NewGuid(),
                    ["Notes"] = null,
                    ["UserId"] = Guid.NewGuid(),
                    ["UserEmail"] = "ana@t.local",
                    ["OccurredAt"] = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Unspecified)
                }),
                FakeResultSet.FromScalar(1));

        var response = await new GetCustomerStatusHistoryQueryHandler(ctx.Factory.Object, ctx.CurrentUser.Object, ctx.Pagination)
            .Handle(new GetCustomerStatusHistoryQuery(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        var item = response.Data!.Items.Single();
        item.ActionName.Should().Be("Deactivated");
        item.SourceName.Should().Be("PersonDeactivated");
        item.OccurredAt.Kind.Should().Be(DateTimeKind.Utc);
        response.Data.TotalPages.Should().Be(1);
        ctx.Connection.LastCommandText.Should()
            .Contain("FROM customer.customerstatushistory h")
            .And.Contain("ORDER BY h.OccurredAt DESC");
    }

    [Fact]
    public async Task GetStatusHistory_ForAnUnknownCustomer_ShouldAnswerNotFound()
    {
        var ctx = new Context(CompanyId);
        ctx.Connection.SetScalarResults(false);

        var response = await new GetCustomerStatusHistoryQueryHandler(ctx.Factory.Object, ctx.CurrentUser.Object, ctx.Pagination)
            .Handle(new GetCustomerStatusHistoryQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("CUSTOMER_NOT_FOUND");
    }
}
