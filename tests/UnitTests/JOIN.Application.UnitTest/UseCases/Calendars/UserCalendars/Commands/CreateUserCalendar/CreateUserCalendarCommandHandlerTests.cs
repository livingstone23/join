using JOIN.Application.DTO.Calendars;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Calendars.UserCalendars;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.CreateUserCalendar;
using JOIN.Domain.Calendars;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.UserCalendars.Commands.CreateUserCalendar;

/// <summary>
/// Unit tests for <see cref="CreateUserCalendarCommandHandler"/> (SPEC 44 F10).
/// </summary>
public sealed class CreateUserCalendarCommandHandlerTests
{
    /// <summary>
    /// The duplicate-name guard (<c>CALENDAR_NAME_DUPLICATE</c>) must short-circuit before the insert.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameIsAlreadyTaken_ShouldReturnCalendarNameDuplicate()
    {
        var ctx = new CreateUserCalendarTestContext();
        ctx.SetActiveTenant();
        var existingCalendar = new UserCalendar
        {
            CompanyId = ctx.CompanyId,
            OwnerUserId = ctx.UserId,
            Name = "Work",
            Color = "#3b82f6",
            IsActive = true,
            SortOrder = 0
        };
        EntityIdSetter.SetId(existingCalendar, Guid.NewGuid());
        var userCalendarRepository = ctx.SeedCalendars(existingCalendar);
        var existingConfig = new CalendarUserConfiguration
        {
            CompanyId = ctx.CompanyId,
            UserId = ctx.UserId,
            DefaultCalendarId = Guid.NewGuid()
        };
        EntityIdSetter.SetId(existingConfig, Guid.NewGuid());
        ctx.SeedConfiguration(existingConfig);

        var response = await ctx.CreateHandler().Handle(
            new CreateUserCalendarCommand { Name = "Work", Color = "#111111", SortOrder = 1 },
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CalendarNameDuplicate);
        response.Errors.Should().Contain("You already have a calendar with this name.");

        userCalendarRepository.Verify(x => x.InsertAsync(It.IsAny<UserCalendar>()), Times.Never);
        ctx.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Reusable mocks for the create handler tests.
    /// </summary>
    private sealed class CreateUserCalendarTestContext
    {
        public CreateUserCalendarTestContext()
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
        /// Registers the in-memory <see cref="UserCalendar"/> table (used by
        /// <c>IsNameTakenAsync</c>) and returns the mock so the test can verify calls on it.
        /// </summary>
        public Mock<IGenericRepository<UserCalendar>> SeedCalendars(params UserCalendar[] calendars)
        {
            var repository = UnitOfWorkMock.SetupRepositoryRows(calendars);
            repository.Setup(x => x.InsertAsync(It.IsAny<UserCalendar>())).ReturnsAsync(true);
            return repository;
        }

        /// <summary>
        /// Registers the user's <see cref="CalendarUserConfiguration"/> (the user is provisioned) so that
        /// <c>defaultCoordinator.GetConfigurationAsync</c> returns the configuration.
        /// </summary>
        public Mock<IGenericRepository<CalendarUserConfiguration>> SeedConfiguration(CalendarUserConfiguration configuration)
        {
            var repository = UnitOfWorkMock.SetupRepositoryRows(new[] { configuration });
            repository.Setup(x => x.GetAsync(configuration.Id)).ReturnsAsync(configuration);
            repository.Setup(x => x.UpdateAsync(configuration)).ReturnsAsync(true);
            return repository;
        }

        public CreateUserCalendarCommandHandler CreateHandler()
        {
            var coordinator = new CalendarUserDefaultCoordinator(UnitOfWorkMock.Object);
            var context = new UserCalendarCommandContext(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                new CalendarCompanyGuard(UnitOfWorkMock.Object),
                coordinator,
                CalendarMapperMock.Object);
            return new CreateUserCalendarCommandHandler(UnitOfWorkMock.Object, context, coordinator);
        }
    }
}
