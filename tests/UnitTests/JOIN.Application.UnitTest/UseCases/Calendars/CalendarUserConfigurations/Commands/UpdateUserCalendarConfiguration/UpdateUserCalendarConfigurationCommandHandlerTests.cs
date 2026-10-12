using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateUserCalendarConfiguration;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateUserCalendarConfiguration;

/// <summary>
/// Contains the unit tests for <see cref="UpdateUserCalendarConfigurationCommandHandler"/>: tenant guard, configuration
/// guard, the work-days branch, and the out-of-range guard for the user's own hours.
/// </summary>
public sealed class UpdateUserCalendarConfigurationCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant context is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new UpdateUserCalendarConfigurationCommandTestContext();
        var handler = context.CreateHandler();

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when the company has no calendar configuration yet.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyHasNoConfiguration_ShouldReturnCompanyNotConfiguredError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateUserCalendarConfigurationCommandTestContext(companyId);
        var handler = context.CreateHandler();

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
    }

    /// <summary>
    /// Verifies the not-found branch when the user has no configuration row yet.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasNoConfiguration_ShouldReturnUserConfigurationNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateUserCalendarConfigurationCommandTestContext(companyId);
        var handler = context.CreateHandler();
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            new CalendarCompany
            {
                CompanyId = companyId,
                TimeZone = "America/Managua",
                IsOpen24Hours = false,
                BusinessHoursStart = new TimeOnly(8, 0),
                BusinessHoursEnd = new TimeOnly(17, 0),
                RescheduledByOthersStatusCompanyId = Guid.NewGuid()
            }
        });
        context.UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarUserConfiguration>());

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.UserConfigurationNotFound);
    }

    /// <summary>
    /// Verifies the out-of-range branch when the user's hours fall outside the company hours.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHoursOutsideCompanyHours_ShouldReturnOutOfRangeError()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new UpdateUserCalendarConfigurationCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand(userId) with
        {
            BusinessHoursStart = new TimeOnly(7, 0),
            BusinessHoursEnd = new TimeOnly(18, 0)
        };
        var existingConfig = SeedExistingConfiguration(companyId, userId, new TimeOnly(8, 0), new TimeOnly(17, 0));
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            new CalendarCompany
            {
                CompanyId = companyId,
                TimeZone = "America/Managua",
                IsOpen24Hours = false,
                BusinessHoursStart = new TimeOnly(8, 0),
                BusinessHoursEnd = new TimeOnly(17, 0),
                RescheduledByOthersStatusCompanyId = Guid.NewGuid()
            }
        });
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { existingConfig });

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.UserHoursOutOfCompanyRange);
        response.Errors.Should().Contain(s => s.Contains("08:00-17:00"));
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: the configuration is updated with the new values and the DTO carries the company
    /// context (zone + effective hours).
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateConfigurationAndReturnDto()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new UpdateUserCalendarConfigurationCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand(userId);
        var existingConfig = SeedExistingConfiguration(companyId, userId, new TimeOnly(8, 0), new TimeOnly(17, 0));
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            new CalendarCompany
            {
                CompanyId = companyId,
                TimeZone = "America/Managua",
                IsOpen24Hours = false,
                BusinessHoursStart = new TimeOnly(8, 0),
                BusinessHoursEnd = new TimeOnly(17, 0),
                RescheduledByOthersStatusCompanyId = Guid.NewGuid()
            }
        });
        // Capture the inner mock so UpdateAsync and the GetAllIncludingDeletedAsync lookup used by the
        // coordinator both resolve to the same instance the handler reaches through GetRepository<T>().
        var configurationRepositoryMock = context.UnitOfWorkMock.SetupRepositoryRows(new[] { existingConfig });
        configurationRepositoryMock.Setup(x => x.GetAsync(existingConfig.Id)).ReturnsAsync(existingConfig);
        configurationRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<CalendarUserConfiguration>()))
            .ReturnsAsync(true);
        context.UserCalendarRepositoryMock.Setup(x => x.GetAsync(existingConfig.DefaultCalendarId)).ReturnsAsync(new UserCalendar { Name = "Personal" });
        context.UserRepositoryMock.Setup(x => x.GetAsync(userId)).ReturnsAsync(new ApplicationUser { FirstName = "Alice", LastName = "Smith", Email = "alice@acme.test" });

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.UserId.Should().Be(userId);
        response.Data.HasCalendar.Should().BeTrue();
        response.Data.CompanyBusinessHoursStart.Should().Be(new TimeOnly(8, 0));
        response.Data.CompanyBusinessHoursEnd.Should().Be(new TimeOnly(17, 0));
        existingConfig.BusinessHoursStart.Should().Be(request.BusinessHoursStart);
        existingConfig.BusinessHoursEnd.Should().Be(request.BusinessHoursEnd);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid update command for reuse across test scenarios.
    /// </summary>
    private UpdateUserCalendarConfigurationCommand CreateValidCommand(Guid? userId = null)
    {
        return new UpdateUserCalendarConfigurationCommand
        {
            UserId = userId ?? _fixture.Create<Guid>(),
            BusinessHoursStart = new TimeOnly(9, 0),
            BusinessHoursEnd = new TimeOnly(16, 0),
            WorkDays = CalendarWorkDays.Weekdays,
            DefaultView = CalendarViewType.Week,
            WeekStartsOn = DayOfWeek.Monday
        };
    }

    /// <summary>
    /// Builds an existing <see cref="CalendarUserConfiguration"/> for the supplied company/user with the given
    /// business hours.
    /// </summary>
    private static CalendarUserConfiguration SeedExistingConfiguration(Guid companyId, Guid userId, TimeOnly start, TimeOnly end)
    {
        var configuration = new CalendarUserConfiguration
        {
            CompanyId = companyId,
            UserId = userId,
            DefaultCalendarId = Guid.NewGuid(),
            BusinessHoursStart = start,
            BusinessHoursEnd = end,
            WorkDays = CalendarWorkDays.Weekdays,
            DefaultView = CalendarViewType.Week,
            WeekStartsOn = DayOfWeek.Monday
        };
        EntityIdSetter.SetId(configuration, Guid.NewGuid());
        return configuration;
    }

    /// <summary>
    /// Holds the reusable mocks for the update handler tests. CalendarCompanyGuard and
    /// CalendarUserDefaultCoordinator are instantiated directly because they are concrete classes built around
    /// <see cref="IUnitOfWork"/>.
    /// </summary>
    private sealed class UpdateUserCalendarConfigurationCommandTestContext
    {
        public UpdateUserCalendarConfigurationCommandTestContext() : this(Guid.Empty) { }

        public UpdateUserCalendarConfigurationCommandTestContext(Guid companyId)
        {
            CompanyId = companyId;
            if (companyId != Guid.Empty)
            {
                CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            }

            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarUserConfiguration>()).Returns(CalendarUserConfigurationRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationUser>()).Returns(UserRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<UserCalendar>()).Returns(UserCalendarRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarCompany>());
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarUserConfiguration>());
        }

        public Guid CompanyId { get; }
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarUserConfiguration>> CalendarUserConfigurationRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserCalendar>> UserCalendarRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public UpdateUserCalendarConfigurationCommandHandler CreateHandler()
        {
            var guard = new CalendarCompanyGuard(UnitOfWorkMock.Object);
            var coordinator = new CalendarUserDefaultCoordinator(UnitOfWorkMock.Object);
            return new UpdateUserCalendarConfigurationCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                guard,
                coordinator);
        }
    }
}