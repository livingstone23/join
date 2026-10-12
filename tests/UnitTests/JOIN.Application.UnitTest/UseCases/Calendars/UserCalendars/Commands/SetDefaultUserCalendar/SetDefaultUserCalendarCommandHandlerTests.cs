using JOIN.Application.DTO.Calendars;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Calendars.UserCalendars;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.SetDefaultUserCalendar;
using JOIN.Domain.Calendars;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.UserCalendars.Commands.SetDefaultUserCalendar;

/// <summary>
/// Unit tests for <see cref="SetDefaultUserCalendarCommandHandler"/> (SPEC 44 F10).
/// </summary>
public sealed class SetDefaultUserCalendarCommandHandlerTests
{
    /// <summary>
    /// The default coordinate is transferred to the chosen calendar: the prior default is cleared and
    /// <see cref="CalendarUserDefaultCoordinator.SetDefaultAsync"/> persists the new value.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCalendarExists_ShouldTransferDefaultAndPersist()
    {
        var ctx = new SetDefaultUserCalendarTestContext();
        var calendarId = Guid.NewGuid();
        var priorDefaultId = Guid.NewGuid();
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
        var configuration = new CalendarUserConfiguration
        {
            CompanyId = ctx.CompanyId,
            UserId = ctx.UserId,
            DefaultCalendarId = priorDefaultId
        };
        EntityIdSetter.SetId(configuration, Guid.NewGuid());
        ctx.SetActiveTenant();
        var configurationRepository = ctx.SeedConfiguration(configuration);
        ctx.SeedCalendars(calendar);

        var response = await ctx.CreateHandler().Handle(
            new SetDefaultUserCalendarCommand(calendarId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Default calendar changed successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(calendarId);
        response.Data.IsDefault.Should().BeTrue();

        // The coordinator must have transferred the default coordinate to the requested calendar
        // (the prior default is no longer referenced) and the change must be persisted.
        configuration.DefaultCalendarId.Should().Be(calendarId);
        configurationRepository.Verify(x => x.UpdateAsync(configuration), Times.Once);
        ctx.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Reusable mocks for the set-default handler tests.
    /// </summary>
    private sealed class SetDefaultUserCalendarTestContext
    {
        public SetDefaultUserCalendarTestContext()
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
        /// Registers the user's <see cref="CalendarUserConfiguration"/> so the coordinator can locate it
        /// via <c>GetConfigurationAsync</c>.
        /// </summary>
        public Mock<IGenericRepository<CalendarUserConfiguration>> SeedConfiguration(CalendarUserConfiguration configuration)
        {
            var repository = UnitOfWorkMock.SetupRepositoryRows(new[] { configuration });
            repository.Setup(x => x.GetAsync(configuration.Id)).ReturnsAsync(configuration);
            repository.Setup(x => x.UpdateAsync(configuration)).ReturnsAsync(true);
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
            return repository;
        }

        public SetDefaultUserCalendarCommandHandler CreateHandler()
        {
            var coordinator = new CalendarUserDefaultCoordinator(UnitOfWorkMock.Object);
            var context = new UserCalendarCommandContext(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                new CalendarCompanyGuard(UnitOfWorkMock.Object),
                coordinator,
                CalendarMapperMock.Object);
            return new SetDefaultUserCalendarCommandHandler(UnitOfWorkMock.Object, context, coordinator);
        }
    }
}
