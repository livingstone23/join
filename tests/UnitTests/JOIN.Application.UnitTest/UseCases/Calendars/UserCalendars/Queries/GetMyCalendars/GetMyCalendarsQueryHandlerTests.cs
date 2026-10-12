using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.UserCalendars.Queries.GetMyCalendars;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.UserCalendars.Queries.GetMyCalendars;

/// <summary>
/// Unit tests for <see cref="GetMyCalendarsQueryHandler"/> (SPEC 44 F10).
/// </summary>
public sealed class GetMyCalendarsQueryHandlerTests
{
    /// <summary>
    /// Without any <c>UserCalendar</c> in the company, the response carries an empty list and
    /// <c>PendingProvisioning = true</c> so the FE can prompt the provisioning flow.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasNoCalendar_ShouldReturnPendingProvisioning()
    {
        var ctx = new GetMyCalendarsTestContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
        ctx.ConnectionFactory.SetResults(FakeResultSet.Empty(
            "Id", "OwnerUserId", "OwnerName", "Name", "Description", "Color",
            "IsActive", "SortOrder", "IsDefault", "CreatedAt"));

        var response = await ctx.Handler.Handle(new GetMyCalendarsQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Calendars retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.PendingProvisioning.Should().BeTrue();
        response.Data.Calendars.Should().BeEmpty();
    }

    /// <summary>
    /// When the user has at least one calendar, the list is returned with the default flag set on the
    /// row whose id matches the user's configuration, and <c>PendingProvisioning = false</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasCalendars_ShouldReturnListWithDefaultFlag()
    {
        var ctx = new GetMyCalendarsTestContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var defaultCalendarId = Guid.NewGuid();
        var otherCalendarId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
        ctx.ConnectionFactory.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = defaultCalendarId,
                ["OwnerUserId"] = userId,
                ["OwnerName"] = "Jane Doe",
                ["Name"] = "Default",
                ["Description"] = "Primary",
                ["Color"] = "#3b82f6",
                ["IsActive"] = true,
                ["SortOrder"] = 0,
                ["IsDefault"] = true,
                ["CreatedAt"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Dictionary<string, object?>
            {
                ["Id"] = otherCalendarId,
                ["OwnerUserId"] = userId,
                ["OwnerName"] = "Jane Doe",
                ["Name"] = "Personal",
                ["Description"] = null,
                ["Color"] = "#10b981",
                ["IsActive"] = true,
                ["SortOrder"] = 1,
                ["IsDefault"] = false,
                ["CreatedAt"] = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            }));

        var response = await ctx.Handler.Handle(new GetMyCalendarsQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.PendingProvisioning.Should().BeFalse();
        response.Data.Calendars.Should().HaveCount(2);
        response.Data.Calendars.Single(c => c.Id == defaultCalendarId).IsDefault.Should().BeTrue();
        response.Data.Calendars.Single(c => c.Id == otherCalendarId).IsDefault.Should().BeFalse();
        response.Data.Calendars.First().OwnerName.Should().Be("Jane Doe");
    }

    /// <summary>
    /// Reusable test doubles for the query handler tests.
    /// </summary>
    private sealed class GetMyCalendarsTestContext
    {
        public FakeSqlConnectionFactory ConnectionFactory { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetMyCalendarsQueryHandler Handler => new(ConnectionFactory, CurrentUserServiceMock.Object);
    }
}
