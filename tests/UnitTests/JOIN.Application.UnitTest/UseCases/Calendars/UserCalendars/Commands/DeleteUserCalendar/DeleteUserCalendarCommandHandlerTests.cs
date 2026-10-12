using JOIN.Application.DTO.Calendars;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Calendars.UserCalendars;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.DeleteUserCalendar;
using JOIN.Domain.Calendars;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.UserCalendars.Commands.DeleteUserCalendar;

/// <summary>
/// Unit tests for <see cref="DeleteUserCalendarCommandHandler"/> (SPEC 44 F10).
/// </summary>
public sealed class DeleteUserCalendarCommandHandlerTests
{
    /// <summary>
    /// Deleting the default calendar returns <c>CALENDAR_IS_DEFAULT</c> (SPEC 44 F10) without writing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCalendarIsDefault_ShouldReturnCalendarIsDefault()
    {
        var ctx = new DeleteUserCalendarTestContext();
        var calendarId = Guid.NewGuid();
        var calendar = new UserCalendar
        {
            CompanyId = ctx.CompanyId,
            OwnerUserId = ctx.UserId,
            Name = "Default",
            Color = "#3b82f6",
            IsActive = true,
            SortOrder = 0
        };
        EntityIdSetter.SetId(calendar, calendarId);
        ctx.SetActiveTenant();
        ctx.SeedConfiguration(defaultCalendarId: calendarId);
        var userCalendarRepository = ctx.SeedCalendars(calendar);

        var response = await ctx.CreateHandler().Handle(
            new DeleteUserCalendarCommand(calendarId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CalendarIsDefault);
        response.Errors.Should().Contain(s => s.Contains("default calendar cannot be deactivated"));

        userCalendarRepository.Verify(x => x.UpdateAsync(It.IsAny<UserCalendar>()), Times.Never);
        ctx.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        calendar.GcRecord.Should().Be(0);
    }

    /// <summary>
    /// A calendar with upcoming non-cancelled events is blocked (<c>CALENDAR_HAS_ACTIVE_EVENTS</c>).
    /// </summary>
    [Fact]
    public async Task Handle_WhenCalendarHasUpcomingEvents_ShouldReturnCalendarHasActiveEvents()
    {
        var ctx = new DeleteUserCalendarTestContext();
        var calendarId = Guid.NewGuid();
        var calendar = new UserCalendar
        {
            CompanyId = ctx.CompanyId,
            OwnerUserId = ctx.UserId,
            Name = "Work",
            Color = "#3b82f6",
            IsActive = true,
            SortOrder = 0
        };
        EntityIdSetter.SetId(calendar, calendarId);
        ctx.SetActiveTenant();
        ctx.SeedConfiguration(defaultCalendarId: Guid.NewGuid());
        var userCalendarRepository = ctx.SeedCalendars(calendar);
        ctx.ConfigureUpcomingEvents(calendarId, 3);

        var response = await ctx.CreateHandler().Handle(
            new DeleteUserCalendarCommand(calendarId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CalendarHasActiveEvents);
        response.Errors.Should().Contain(s => s.Contains("3 upcoming activities"));

        userCalendarRepository.Verify(x => x.UpdateAsync(It.IsAny<UserCalendar>()), Times.Never);
        ctx.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        calendar.GcRecord.Should().Be(0);
    }

    /// <summary>
    /// A non-default calendar with no upcoming events is soft-deleted successfully.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCalendarIsRetirable_ShouldDeleteAndReturnSuccess()
    {
        var ctx = new DeleteUserCalendarTestContext();
        var calendarId = Guid.NewGuid();
        var calendar = new UserCalendar
        {
            CompanyId = ctx.CompanyId,
            OwnerUserId = ctx.UserId,
            Name = "Personal",
            Color = "#10b981",
            IsActive = true,
            SortOrder = 1
        };
        EntityIdSetter.SetId(calendar, calendarId);
        UserCalendar? updatedCalendar = null;
        ctx.SetActiveTenant();
        ctx.SeedConfiguration(defaultCalendarId: Guid.NewGuid());
        var userCalendarRepository = ctx.SeedCalendars(calendar);
        ctx.ConfigureUpcomingEvents(calendarId, 0);
        userCalendarRepository
            .Setup(x => x.UpdateAsync(It.IsAny<UserCalendar>()))
            .Callback<UserCalendar>(c => updatedCalendar = c)
            .ReturnsAsync(true);

        var response = await ctx.CreateHandler().Handle(
            new DeleteUserCalendarCommand(calendarId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeTrue();
        response.Message.Should().Be("Calendar deleted successfully.");

        updatedCalendar.Should().NotBeNull();
        updatedCalendar!.Id.Should().Be(calendarId);
        updatedCalendar.GcRecord.Should().NotBe(0);

        userCalendarRepository.Verify(x => x.UpdateAsync(It.IsAny<UserCalendar>()), Times.Once);
        ctx.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Reusable mocks for the delete handler tests.
    /// </summary>
    private sealed class DeleteUserCalendarTestContext
    {
        public DeleteUserCalendarTestContext()
        {
            var calendarCompany = new CalendarCompany
            {
                CompanyId = CompanyId,
                TimeZone = "UTC",
                IsOpen24Hours = true,
                DefaultCalendarName = "Mi calendario",
                DefaultActivityUserId = UserId,
                RescheduledByOthersStatusCompanyId = Guid.NewGuid()
            };
            EntityIdSetter.SetId(calendarCompany, Guid.NewGuid());
            UnitOfWorkMock.SetupRepositoryRows(new[] { calendarCompany });
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            UnitOfWorkMock.Setup(x => x.CalendarEvents).Returns(CalendarEventsRepositoryMock.Object);

            CalendarEventsRepositoryMock
                .Setup(x => x.CountUpcomingActiveEventsInCalendarAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            CalendarMapperMock
                .Setup(x => x.ToDto(It.IsAny<UserCalendar>()))
                .Returns((UserCalendar c) => new UserCalendarDto
                {
                    Id = c.Id,
                    OwnerUserId = c.OwnerUserId,
                    Name = c.Name,
                    Description = c.Description,
                    Color = c.Color,
                    IsActive = c.IsActive,
                    SortOrder = c.SortOrder,
                    IsDefault = false,
                    CreatedAt = c.Created
                });
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ICalendarMapper> CalendarMapperMock { get; } = new();
        public Mock<ICalendarEventRepository> CalendarEventsRepositoryMock { get; } = new();

        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();

        public void SetActiveTenant()
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(UserId.ToString());
        }

        /// <summary>
        /// Sets up the user's <see cref="CalendarUserConfiguration"/> pointing at
        /// <paramref name="defaultCalendarId"/>.
        /// </summary>
        public Mock<IGenericRepository<CalendarUserConfiguration>> SeedConfiguration(Guid defaultCalendarId)
        {
            var configuration = new CalendarUserConfiguration
            {
                CompanyId = CompanyId,
                UserId = UserId,
                DefaultCalendarId = defaultCalendarId
            };
            EntityIdSetter.SetId(configuration, Guid.NewGuid());
            var repository = UnitOfWorkMock.SetupRepositoryRows(new[] { configuration });
            repository.Setup(x => x.GetAsync(configuration.Id)).ReturnsAsync(configuration);
            return repository;
        }

        /// <summary>
        /// Registers the in-memory <see cref="UserCalendar"/> table (used by <c>FindOwnAsync</c>).
        /// </summary>
        public Mock<IGenericRepository<UserCalendar>> SeedCalendars(params UserCalendar[] calendars)
        {
            var repository = UnitOfWorkMock.SetupRepositoryRows(calendars);
            foreach (var calendar in calendars)
            {
                repository.Setup(x => x.GetAsync(calendar.Id)).ReturnsAsync(calendar);
            }
            repository.Setup(x => x.UpdateAsync(It.IsAny<UserCalendar>())).ReturnsAsync(true);
            return repository;
        }

        /// <summary>
        /// Overrides the upcoming-event counter for the given calendar.
        /// </summary>
        public void ConfigureUpcomingEvents(Guid calendarId, int count)
        {
            CalendarEventsRepositoryMock
                .Setup(x => x.CountUpcomingActiveEventsInCalendarAsync(calendarId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(count);
        }

        public DeleteUserCalendarCommandHandler CreateHandler()
        {
            var coordinator = new CalendarUserDefaultCoordinator(UnitOfWorkMock.Object);
            var context = new UserCalendarCommandContext(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                new CalendarCompanyGuard(UnitOfWorkMock.Object),
                coordinator,
                CalendarMapperMock.Object);
            return new DeleteUserCalendarCommandHandler(UnitOfWorkMock.Object, context);
        }
    }
}
