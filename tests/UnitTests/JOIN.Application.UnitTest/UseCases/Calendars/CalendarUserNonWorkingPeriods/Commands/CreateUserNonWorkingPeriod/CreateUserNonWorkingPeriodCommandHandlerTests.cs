using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.Mappings;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateUserNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateUserNonWorkingPeriod;

/// <summary>
/// Contains the unit tests for <see cref="CreateUserNonWorkingPeriodCommandHandler"/>: tenant guard, configuration
/// guard, user-membership guard, overlap detection, the > 366 day range guard, and the affected-events list.
/// </summary>
public sealed class CreateUserNonWorkingPeriodCommandHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant context is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new CreateUserNonWorkingPeriodCommandTestContext();
        var handler = context.CreateHandler();

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
        context.CalendarUserNonWorkingPeriodRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarUserNonWorkingPeriod>()), Times.Never);
    }

    /// <summary>
    /// Verifies the not-configured branch when the company has no calendar configuration yet.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyHasNoConfiguration_ShouldReturnCompanyNotConfiguredError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new CreateUserNonWorkingPeriodCommandTestContext(companyId);
        var handler = context.CreateHandler();

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
    }

    /// <summary>
    /// Verifies the not-member branch when the user is not part of the company.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIsNotCompanyMember_ShouldReturnUserNotInCompanyError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new CreateUserNonWorkingPeriodCommandTestContext(companyId);
        var handler = context.CreateHandler();
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { SeedCompany(companyId) });
        context.UnitOfWorkMock.SetupRepositoryRows(Array.Empty<UserCompany>());

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.UserNotInCompany);
        response.Errors.Should().Contain("The user is not an active user of the current company.");
    }

    /// <summary>
    /// Verifies the range-too-large branch when the period exceeds 366 days.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRangeExceeds366Days_ShouldReturnInvalidEventTimesError()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new CreateUserNonWorkingPeriodCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand(userId) with
        {
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2027, 2, 1)
        };
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { SeedCompany(companyId) });
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { new UserCompany { UserId = userId, CompanyId = companyId } });

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.InvalidEventTimes);
        response.Errors.Should().Contain(s => s.Contains("366"));
        context.CalendarUserNonWorkingPeriodRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarUserNonWorkingPeriod>()), Times.Never);
    }

    /// <summary>
    /// Verifies the overlap branch when another active period of the user covers the same dates.
    /// </summary>
    [Fact]
    public async Task Handle_WhenExistingPeriodOverlaps_ShouldReturnOverlapError()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new CreateUserNonWorkingPeriodCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand(userId) with
        {
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 6, 10)
        };
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { SeedCompany(companyId) });
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { new UserCompany { UserId = userId, CompanyId = companyId } });
        context.UnitOfWorkMock.SetupRepositoryRows(new[]
        {
            new CalendarUserNonWorkingPeriod
            {
                CompanyId = companyId,
                UserId = userId,
                StartDate = new DateOnly(2026, 6, 5),
                EndDate = new DateOnly(2026, 6, 15)
            }
        });

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.NonWorkingPeriodOverlap);
        response.Errors.Should().Contain("The period overlaps another period of the user.");
        context.CalendarUserNonWorkingPeriodRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarUserNonWorkingPeriod>()), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: the entity is persisted, the affected-events list is returned empty (no rows from
    /// the schedule query), and the message confirms the period was registered.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreatePeriodAndReturnAffectedEvents()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new CreateUserNonWorkingPeriodCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand(userId) with
        {
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 6, 5)
        };
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { SeedCompany(companyId) });
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { new UserCompany { UserId = userId, CompanyId = companyId } });
        // Capture the inner mock SetupRepositoryRows wired for CalendarUserNonWorkingPeriod so InsertAsync
        // and the overlap-detection GetAllIncludingDeletedAsync operate on the same instance the writer
        // reaches through GetRepository<T>().
        var periodRepositoryMock = context.UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarUserNonWorkingPeriod>());
        context.UserRepositoryMock.Setup(x => x.GetAsync(userId)).ReturnsAsync(new ApplicationUser { FirstName = "Alice", LastName = "Smith" });

        CalendarUserNonWorkingPeriod? inserted = null;
        periodRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarUserNonWorkingPeriod>()))
            .Callback<CalendarUserNonWorkingPeriod>(e => inserted = e)
            .ReturnsAsync(true);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Non-working period registered successfully.");
        response.Data!.Period.UserName.Should().Be("Alice Smith");
        response.Data.Period.StartDate.Should().Be(request.StartDate);
        response.Data.Period.EndDate.Should().Be(request.EndDate);
        response.Data.AffectedEvents.Should().BeEmpty();
        inserted.Should().NotBeNull();
        inserted!.CompanyId.Should().Be(companyId);
        inserted.UserId.Should().Be(userId);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the schedule rows returned by the events repository are mapped to <c>AffectedEvents</c> on the
    /// response, with one entry per blocking-time occurrence.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEventsFallInRange_ShouldListAffectedEvents()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new CreateUserNonWorkingPeriodCommandTestContext(companyId);
        var handler = context.CreateHandler();
        var request = CreateValidCommand(userId) with
        {
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 6, 5)
        };
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { SeedCompany(companyId) });
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { new UserCompany { UserId = userId, CompanyId = companyId } });
        context.UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarUserNonWorkingPeriod>());
        context.UserRepositoryMock.Setup(x => x.GetAsync(userId)).ReturnsAsync(new ApplicationUser { FirstName = "Alice", LastName = "Smith" });
        context.CalendarUserNonWorkingPeriodRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<CalendarUserNonWorkingPeriod>())).ReturnsAsync(true);

        var eventId = Guid.NewGuid();
        var start = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);
        context.CalendarEventsRepositoryMock
            .Setup(x => x.GetScheduleRowsAsync(companyId, userId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CalendarEventScheduleRow(eventId, null, null, "Standup", start, end, false, null, null, false, true)
            });

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.AffectedEvents.Should().HaveCount(1);
        response.Data.AffectedEvents[0].EventId.Should().Be(eventId);
        response.Data.AffectedEvents[0].Title.Should().Be("Standup");
    }

    /// <summary>
    /// Creates a valid create command for reuse across test scenarios.
    /// </summary>
    private CreateUserNonWorkingPeriodCommand CreateValidCommand(Guid? userId = null)
    {
        return new CreateUserNonWorkingPeriodCommand
        {
            UserId = userId ?? _fixture.Create<Guid>(),
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 6, 3),
            Reason = CalendarNonWorkingReason.Vacation,
            Notes = "Annual leave"
        };
    }

    /// <summary>
    /// Builds a configured company row so the writer's <c>OpenAsync</c> succeeds.
    /// </summary>
    private static CalendarCompany SeedCompany(Guid companyId)
    {
        var company = new CalendarCompany
        {
            CompanyId = companyId,
            TimeZone = "America/Managua",
            IsOpen24Hours = false,
            BusinessHoursStart = new TimeOnly(8, 0),
            BusinessHoursEnd = new TimeOnly(17, 0),
            DefaultCalendarName = "Mi calendario",
            RescheduledByOthersStatusCompanyId = Guid.NewGuid()
        };
        EntityIdSetter.SetId(company, Guid.NewGuid());
        return company;
    }

    /// <summary>
    /// Holds the reusable mocks for the create handler tests. <see cref="CalendarNonWorkingPeriodWriter"/> is
    /// instantiated directly with a mocked <see cref="ICalendarRecurrenceExpander"/> and <see cref="ICalendarMapper"/>.
    /// </summary>
    private sealed class CreateUserNonWorkingPeriodCommandTestContext
    {
        public CreateUserNonWorkingPeriodCommandTestContext() : this(Guid.Empty) { }

        public CreateUserNonWorkingPeriodCommandTestContext(Guid companyId)
        {
            CompanyId = companyId;
            if (companyId != Guid.Empty)
            {
                CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            }

            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarUserNonWorkingPeriod>()).Returns(CalendarUserNonWorkingPeriodRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationUser>()).Returns(UserRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            UnitOfWorkMock.Setup(x => x.CalendarEvents).Returns(CalendarEventsRepositoryMock.Object);

            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarCompany>());
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<UserCompany>());
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<CalendarUserNonWorkingPeriod>());

            CalendarEventsRepositoryMock
                .Setup(x => x.GetScheduleRowsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<CalendarEventScheduleRow>());

            MapperMock.Setup(x => x.ToDto(It.IsAny<CalendarUserNonWorkingPeriod>()))
                .Returns((CalendarUserNonWorkingPeriod e) => new JOIN.Application.DTO.Calendars.CalendarUserNonWorkingPeriodDto
                {
                    Id = e.Id,
                    UserId = e.UserId,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    Reason = (int)e.Reason,
                    Notes = e.Notes,
                    CreatedAt = e.Created
                });
        }

        public Guid CompanyId { get; }
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<CalendarUserNonWorkingPeriod>> CalendarUserNonWorkingPeriodRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = new();
        public Mock<ICalendarEventRepository> CalendarEventsRepositoryMock { get; } = new();
        public Mock<ICalendarRecurrenceExpander> ExpanderMock { get; } = new();
        public Mock<ICalendarMapper> MapperMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public CreateUserNonWorkingPeriodCommandHandler CreateHandler()
        {
        ExpanderMock.Setup(x => x.ValidateRule(It.IsAny<string>())).Returns((string r) => CalendarRecurrenceRule.Parse(r));
            var guard = new CalendarCompanyGuard(UnitOfWorkMock.Object);
            var writer = new CalendarNonWorkingPeriodWriter(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                guard,
                ExpanderMock.Object,
                MapperMock.Object);
            return new CreateUserNonWorkingPeriodCommandHandler(writer);
        }
    }
}