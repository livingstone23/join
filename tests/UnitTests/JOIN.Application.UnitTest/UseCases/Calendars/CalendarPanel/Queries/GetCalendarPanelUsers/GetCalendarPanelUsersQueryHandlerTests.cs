using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelUsers;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelUsers;

/// <summary>
/// SPEC 44 F10 panel — <c>GET /CalendarPanel/users</c>. Dapper call returns only members of the
/// caller's <c>CompanyId</c> with active accounts; the page/pageSize inputs are normalized through
/// <see cref="PaginationSettings"/>, the search filter trims and wraps with wildcards and
/// <c>onlyWithCalendar</c> drops users without a <c>UserConfigurations</c> row in the company.
/// </summary>
public sealed class GetCalendarPanelUsersQueryHandlerTests
{
    private const string UserColumns =
        "UserId;Name;Email;HasCalendar";

    /// <summary>
    /// Missing <c>CompanyId</c> short-circuits with <c>COMPANY_REQUIRED</c>; no connection is opened.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var ctx = new GetCalendarPanelUsersTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var response = await ctx.Handler.Handle(new GetCalendarPanelUsersQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        ctx.ConnectionFactory.LastCommandText.Should().BeEmpty();
    }

    /// <summary>
    /// An empty page binds <c>Offset = 0</c> and the configured default page size, and returns the items
    /// (<c>Name</c> trimmed) plus the total count from the second Dapper grid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPageIsOne_ShouldApplyDefaultPageSizeAndTrimNames()
    {
        var ctx = new GetCalendarPanelUsersTestContext();
        var userId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        ctx.ConnectionFactory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["UserId"] = userId,
                ["Name"] = "Ada Lovelace ",
                ["Email"] = "ada@x.com",
                ["HasCalendar"] = true
            }),
            FakeResultSet.FromScalar(1));

        var response = await ctx.Handler.Handle(new GetCalendarPanelUsersQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.Items.First().Name.Should().Be("Ada Lovelace");
        response.Data.PageNumber.Should().Be(1);
        response.Data.PageSize.Should().Be(ctx.Pagination.DefaultPageSize);
        response.Data.TotalCount.Should().Be(1);
    }

    /// <summary>
    /// A search wraps the trimmed value with wildcards before binding it to <c>@Search</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSearchProvided_ShouldBindWildcardParameter()
    {
        var ctx = new GetCalendarPanelUsersTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        ctx.ConnectionFactory.SetResults(FakeResultSet.Empty(UserColumns.Split(';')), FakeResultSet.FromScalar(0));

        var response = await ctx.Handler.Handle(new GetCalendarPanelUsersQuery(Search: "  ada  "), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        ctx.ConnectionFactory.CapturedParameters["Search"].Should().Be("%ada%");
    }

    /// <summary>
    /// With <c>OnlyWithCalendar = true</c> the SQL gains <c>AND c.Id IS NOT NULL</c>; the captured command
    /// contains that clause.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOnlyWithCalendarIsTrue_ShouldAddHasCalendarFilter()
    {
        var ctx = new GetCalendarPanelUsersTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        ctx.ConnectionFactory.SetResults(FakeResultSet.Empty(UserColumns.Split(';')), FakeResultSet.FromScalar(0));

        var response = await ctx.Handler.Handle(new GetCalendarPanelUsersQuery(OnlyWithCalendar: true), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        ctx.ConnectionFactory.LastCommandText.Should().Contain("c.Id IS NOT NULL");
    }

    /// <summary>
    /// The handler binds <c>Offset = (pageNumber - 1) * pageSize</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPageNumberIsTwo_ShouldComputeOffsetFromPageAndSize()
    {
        var ctx = new GetCalendarPanelUsersTestContext();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        ctx.ConnectionFactory.SetResults(FakeResultSet.Empty(UserColumns.Split(';')), FakeResultSet.FromScalar(0));

        var response = await ctx.Handler.Handle(new GetCalendarPanelUsersQuery(PageNumber: 3, PageSize: 5), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        ctx.ConnectionFactory.CapturedParameters["Offset"].Should().Be(10);
        ctx.ConnectionFactory.CapturedParameters["PageSize"].Should().Be(5);
    }

    /// <summary>
    /// Reusable mocks for the panel users query.
    /// </summary>
    private sealed class GetCalendarPanelUsersTestContext
    {
        public GetCalendarPanelUsersTestContext()
        {
            CompanyId = Guid.NewGuid();
            Pagination = new PaginationSettings();
        }

        public Guid CompanyId { get; }
        public PaginationSettings Pagination { get; }

        public FakeSqlConnectionFactory ConnectionFactory { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetCalendarPanelUsersQueryHandler Handler => new(
            ConnectionFactory,
            Options.Create(Pagination),
            CurrentUserServiceMock.Object);
    }
}
