using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarActivityIndex;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarActivityIndex;

/// <summary>
/// SPEC 68 — visibility context for the activity index. Decides whether the current user can list activities of
/// other members of the company (<c>CanViewOthersActivities</c>, per user, default <c>false</c>). When the user has
/// no configuration row the flag stays <c>false</c> so a freshly provisioned user cannot see other people's
/// activities until an administrator flips it.
/// </summary>
public sealed class CalendarActivityVisibilityTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// No <c>CompanyId</c> in the token → unauthenticated context with the flag at <c>false</c>; the index returns
    /// nothing.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenCompanyIdIsEmpty_ReturnsUnauthenticated()
    {
        var context = new VisibilityTestContext(companyId: Guid.Empty, userId: Guid.NewGuid());
        var visibility = context.CreateVisibility();

        var result = await visibility.ResolveAsync(CancellationToken.None);

        result.CompanyId.Should().Be(Guid.Empty);
        result.CurrentUserId.Should().Be(Guid.Empty);
        result.CanViewOthersActivities.Should().BeFalse();
    }

    /// <summary>
    /// Unparseable user id → unauthenticated.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenUserIdIsNotAGuid_ReturnsUnauthenticated()
    {
        var context = new VisibilityTestContext(companyId: Guid.NewGuid(), userIdRaw: "not-a-guid");
        var visibility = context.CreateVisibility();

        var result = await visibility.ResolveAsync(CancellationToken.None);

        result.CompanyId.Should().Be(Guid.Empty);
        result.CurrentUserId.Should().Be(Guid.Empty);
    }

    /// <summary>
    /// User with no configuration row → the flag stays <c>false</c>; the index will restrict to the user's own
    /// activities only.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenUserHasNoConfigurationRow_FlagDefaultsToFalse()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var context = new VisibilityTestContext(companyId, userId);
        context.UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarUserConfiguration>());
        var visibility = context.CreateVisibility();

        var result = await visibility.ResolveAsync(CancellationToken.None);

        result.CompanyId.Should().Be(companyId);
        result.CurrentUserId.Should().Be(userId);
        result.CanViewOthersActivities.Should().BeFalse();
        result.CanViewOthers().Should().BeFalse();
    }

    /// <summary>
    /// Configuration row exists with the flag at <c>false</c> → the index restricts to the user's own activities.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenFlagIsFalse_ReturnsTheUserCanSeeOnlyOwn()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var context = new VisibilityTestContext(companyId, userId);
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            BuildConfiguration(companyId, userId, canViewOthers: false)
        });
        var visibility = context.CreateVisibility();

        var result = await visibility.ResolveAsync(CancellationToken.None);

        result.CanViewOthersActivities.Should().BeFalse();
    }

    /// <summary>
    /// Administrator flipped the flag → the index lists every member's activities of the company.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenFlagIsTrue_ReturnsTheUserCanSeeCompanyWide()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var context = new VisibilityTestContext(companyId, userId);
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            BuildConfiguration(companyId, userId, canViewOthers: true)
        });
        var visibility = context.CreateVisibility();

        var result = await visibility.ResolveAsync(CancellationToken.None);

        result.CanViewOthersActivities.Should().BeTrue();
        result.CanViewOthers().Should().BeTrue();
    }

    /// <summary>
    /// Another user in the same company flipped the flag on themselves; the current user's own flag stays at
    /// <c>false</c> because the lookup filters by user id.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenOtherUserHasFlag_DoesNotLeakToCurrentUser()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var context = new VisibilityTestContext(companyId, userId);
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            BuildConfiguration(companyId, otherUserId, canViewOthers: true)
        });
        var visibility = context.CreateVisibility();

        var result = await visibility.ResolveAsync(CancellationToken.None);

        result.CanViewOthersActivities.Should().BeFalse();
    }

    /// <summary>
    /// Soft-deleted configuration rows are ignored: even when the only row that matches is deleted, the flag
    /// defaults to <c>false</c>.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenOnlyRowIsSoftDeleted_FlagDefaultsToFalse()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var context = new VisibilityTestContext(companyId, userId);
        var configuration = BuildConfiguration(companyId, userId, canViewOthers: true);
        configuration.MarkAsDeleted();
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { configuration });
        var visibility = context.CreateVisibility();

        var result = await visibility.ResolveAsync(CancellationToken.None);

        result.CanViewOthersActivities.Should().BeFalse();
    }

    private static CalendarUserConfiguration BuildConfiguration(Guid companyId, Guid userId, bool canViewOthers)
    {
        var configuration = new CalendarUserConfiguration
        {
            CompanyId = companyId,
            UserId = userId,
            DefaultCalendarId = Guid.NewGuid(),
            WorkDays = CalendarWorkDays.Weekdays,
            DefaultView = CalendarViewType.Week,
            WeekStartsOn = DayOfWeek.Monday,
            CanViewOthersActivities = canViewOthers
        };
        EntityIdSetter.SetId(configuration, _fixture.Create<Guid>());
        return configuration;
    }

    /// <summary>
    /// Reusable mocks for the visibility service. The service has a single dependency
    /// (<see cref="IUnitOfWork"/>) plus <see cref="ICurrentUserService"/>.
    /// </summary>
    private sealed class VisibilityTestContext
    {
        public VisibilityTestContext(Guid companyId, Guid? userId = null, string? userIdRaw = null)
        {
            CompanyId = companyId;
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userIdRaw ?? (userId?.ToString() ?? string.Empty));
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarUserConfiguration>()).Returns(ConfigurationRepositoryMock.Object);
        }

        public Guid CompanyId { get; }
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarUserConfiguration>> ConfigurationRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public CalendarActivityVisibility CreateVisibility() =>
            new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}