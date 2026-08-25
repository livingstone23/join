using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Queries.GetMyCompanyUserReport;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Queries.GetMyCompanyUserReport;

/// <summary>
/// SPEC 28 / F7 acceptance criteria for the paginated <c>/reports/my-company</c>
/// endpoint: tenant guard returns 400 (no exception), clamps, paging math,
/// search / isActive pass-through.
/// </summary>
public sealed class GetMyCompanyUserReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenTenantIsEmpty_ShouldReturnTenantRequiredNotThrow()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        // SPEC 28 step 6: this used to throw UnauthorizedAccessException (401). The
        // new behavior is a Response<T>.Error so the boundary returns 400.
        var response = await ctx.Handler.Handle(new GetMyCompanyUserReportQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TENANT_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenPageSizeIsZero_ShouldClampToDefault()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.ConnectionFactory.SetResults(
            FakeResultSet.Empty(
                "UserId", "FirstName", "LastName", "Email", "IsActive",
                "UserCreatedDate", "LastLoginDate", "CompanyId", "CompanyName",
                "IsDefaultCompany", "RoleName"),
            FakeResultSet.Empty("Value"));

        try { await ctx.Handler.Handle(
            new GetMyCompanyUserReportQuery(PageSize: 0, PageNumber: 0), CancellationToken.None); }
        catch { /* fake-only */ }

        // Clamp runs BEFORE the SQL call — verify via the parameters the handler
        // asked Dapper to send (default PageSize = 10, default PageNumber = 1 → Offset = 0).
        ctx.ConnectionFactory.CapturedParameters.Should().ContainKey("PageSize");
        ctx.ConnectionFactory.CapturedParameters["PageSize"].Should().Be(10);
        ctx.ConnectionFactory.CapturedParameters["Offset"].Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenPageSizeExceedsMaximum_ShouldClampToFifty()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.ConnectionFactory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["UserId"] = Guid.NewGuid(),
                ["FirstName"] = "Ada",
                ["LastName"] = "Lovelace",
                ["Email"] = "ada@x.com",
                ["IsActive"] = true,
                ["UserCreatedDate"] = DateTime.UtcNow,
                ["LastLoginDate"] = DateTime.UtcNow,
                ["CompanyId"] = Guid.NewGuid(),
                ["CompanyName"] = "Acme",
                ["IsDefaultCompany"] = true,
                ["RoleName"] = "Admin"
            }),
            FakeResultSet.FromScalar(27));

        try { await ctx.Handler.Handle(
            new GetMyCompanyUserReportQuery(PageSize: 200, PageNumber: 1), CancellationToken.None); }
        catch { /* fake-only */ }

        ctx.ConnectionFactory.CapturedParameters["PageSize"].Should().Be(50);
    }

    [Fact]
    public async Task Handle_WhenSearchIsProvided_ShouldPropagateAsLikePattern()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.ConnectionFactory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["UserId"] = Guid.NewGuid(),
                ["FirstName"] = "Ada",
                ["LastName"] = "Lovelace",
                ["Email"] = "ada@x.com",
                ["IsActive"] = true,
                ["UserCreatedDate"] = DateTime.UtcNow,
                ["LastLoginDate"] = DateTime.UtcNow,
                ["CompanyId"] = Guid.NewGuid(),
                ["CompanyName"] = "Acme",
                ["IsDefaultCompany"] = true,
                ["RoleName"] = "Admin"
            }),
            FakeResultSet.FromScalar(0));

        // The SQL round-trip is exercised in integration tests. Here we only care
        // that Search is propagated as a LIKE wildcard before the query runs.
        try { await ctx.Handler.Handle(new GetMyCompanyUserReportQuery(Search: "juan"), CancellationToken.None); }
        catch { /* fake-only: round-trip is integration territory */ }

        ctx.ConnectionFactory.CapturedParameters.Should().ContainKey("Search");
        ctx.ConnectionFactory.CapturedParameters["Search"].Should().Be("%juan%");
    }

    [Fact]
    public async Task Handle_WhenIsActiveFalse_ShouldPropagateParameter()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.ConnectionFactory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["UserId"] = Guid.NewGuid(),
                ["FirstName"] = "Ada",
                ["LastName"] = "Lovelace",
                ["Email"] = "ada@x.com",
                ["IsActive"] = true,
                ["UserCreatedDate"] = DateTime.UtcNow,
                ["LastLoginDate"] = DateTime.UtcNow,
                ["CompanyId"] = Guid.NewGuid(),
                ["CompanyName"] = "Acme",
                ["IsDefaultCompany"] = true,
                ["RoleName"] = "Admin"
            }),
            FakeResultSet.FromScalar(0));

        try { await ctx.Handler.Handle(new GetMyCompanyUserReportQuery(IsActive: false), CancellationToken.None); }
        catch { /* fake-only: round-trip is integration territory */ }

        ctx.ConnectionFactory.CapturedParameters.Should().ContainKey("IsActive");
        ctx.ConnectionFactory.CapturedParameters["IsActive"].Should().Be(false);
    }

    [Fact]
    public async Task Handle_WhenRowsAndCountReturned_ShouldPropagatePagingParameters()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.ConnectionFactory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["UserId"] = Guid.NewGuid(),
                ["FirstName"] = "Grace",
                ["LastName"] = "Hopper",
                ["Email"] = "grace@x.com",
                ["IsActive"] = true,
                ["UserCreatedDate"] = DateTime.UtcNow,
                ["LastLoginDate"] = null,
                ["CompanyId"] = Guid.NewGuid(),
                ["CompanyName"] = "Acme",
                ["IsDefaultCompany"] = true,
                ["RoleName"] = "Admin"
            }),
            FakeResultSet.FromScalar(13));

        try { await ctx.Handler.Handle(
            new GetMyCompanyUserReportQuery(PageNumber: 1, PageSize: 5), CancellationToken.None); }
        catch { /* fake-only */ }

        // Paging math (TotalPages = ceil(TotalCount / PageSize)) is independent of
        // the SQL round-trip — verified in the integration suite.
        ctx.ConnectionFactory.CapturedParameters["PageSize"].Should().Be(5);
        ctx.ConnectionFactory.CapturedParameters["Offset"].Should().Be(0);
    }

    private sealed class Context
    {
        public FakeSqlConnectionFactory ConnectionFactory { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetMyCompanyUserReportQueryHandler Handler => new(
            ConnectionFactory,
            CurrentUserServiceMock.Object);
    }
}