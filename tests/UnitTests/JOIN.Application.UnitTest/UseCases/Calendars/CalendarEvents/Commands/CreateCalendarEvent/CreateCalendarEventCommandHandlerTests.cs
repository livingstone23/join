using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.CreateCalendarEvent;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;
using JOIN.Domain.Customers;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEvents.Commands.CreateCalendarEvent;

/// <summary>
/// SPEC 44 F10 / CalendarEvents group — create handler drives
/// <see cref="CalendarEventCommandProcessor"/> with seeded company, calendar, activity, and status. Verifies the
/// rules the spec calls out explicitly: initial status from the activity type, default duration from the
/// activity type when <c>end</c> is missing, all-day UTC computed from the company zone, link validation, and the
/// <see cref="CalendarEventLogAction.Created"/> log row.
/// </summary>
public sealed class CreateCalendarEventCommandHandlerTests
{
    private static readonly Fixture _fixture = new();
    private const string TimeZoneId = "America/Managua";

    /// <summary>
    /// Initial status of the activity comes from
    /// <see cref="CalendarActivityCompany.InitialCalendarStatusCompanyId"/>, never from the request body.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCreating_ShouldUseInitialStatusFromActivityType()
    {
        var context = new CreateCalendarEventTestContext();
        var command = context.BuildValidCommand();
        var expectedInitialStatusId = context.ActivityCompany.InitialCalendarStatusCompanyId;

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Status.Id.Should().Be(expectedInitialStatusId);
        context.InsertedEvent!.CalendarStatusCompanyId.Should().Be(expectedInitialStatusId);
    }

    /// <summary>
    /// Missing <c>end</c> uses the activity's <c>DefaultDurationMinutes</c>; the resulting
    /// <c>endUtc - startUtc</c> equals the configured duration.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEndIsMissing_ShouldDefaultToActivityDuration()
    {
        var context = new CreateCalendarEventTestContext();
        var start = new DateTimeOffset(2026, 6, 15, 14, 0, 0, TimeSpan.Zero);
        var command = context.BuildValidCommand() with { Start = start, End = null };

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var duration = context.ActivityCompany.DefaultDurationMinutes;
        (response.Data!.EndUtc - response.Data.StartUtc).TotalMinutes.Should().Be(duration);
    }

    /// <summary>
    /// All-day activities use local midnight of the company time zone as UTC start, and local midnight of the
    /// day after the end date as exclusive UTC end.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAllDay_ShouldComputeUtcFromCompanyTimeZone()
    {
        var context = new CreateCalendarEventTestContext();
        var startDate = new DateOnly(2026, 6, 15);
        var command = context.BuildValidCommand() with
        {
            IsAllDay = true,
            Start = null,
            End = null,
            StartDate = startDate,
            EndDate = new DateOnly(2026, 6, 16)
        };

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var expectedStart = TimeZoneInfo.ConvertTimeToUtc(startDate.ToDateTime(TimeOnly.MinValue), zone);
        var expectedEnd = TimeZoneInfo.ConvertTimeToUtc(new DateOnly(2026, 6, 17).ToDateTime(TimeOnly.MinValue), zone);
        response.Data!.StartUtc.Should().Be(expectedStart);
        response.Data.EndUtc.Should().Be(expectedEnd);
    }

    /// <summary>
    /// <c>requesterPersonId</c> that points to another company answers <c>CALENDAR_INVALID_LINK</c>; the row is
    /// not inserted and no log row is written.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequesterPersonIsInAnotherCompany_ShouldReturnInvalidLink()
    {
        var context = new CreateCalendarEventTestContext();
        var command = context.BuildValidCommand() with
        {
            RequesterPersonId = Guid.NewGuid()
        };
        // PersonRepository returns null because the foreign-company person is filtered out by CompanyId != tenant.
        context.PersonRepositoryMock
            .Setup(x => x.GetAsync(command.RequesterPersonId!.Value))
            .ReturnsAsync((Person?)null);

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.InvalidLink);
        context.CalendarEventsRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    /// <summary>
    /// <c>customerId</c> pointing to another company also fails with <c>CALENDAR_INVALID_LINK</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCustomerIsInAnotherCompany_ShouldReturnInvalidLink()
    {
        var context = new CreateCalendarEventTestContext();
        var command = context.BuildValidCommand() with
        {
            CustomerId = Guid.NewGuid()
        };
        context.CustomerRepositoryMock
            .Setup(x => x.GetAsync(command.CustomerId!.Value))
            .ReturnsAsync((Customer?)null);

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.InvalidLink);
    }

    /// <summary>
    /// <c>ticketId</c> from another company fails with the same code.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketIsInAnotherCompany_ShouldReturnInvalidLink()
    {
        var context = new CreateCalendarEventTestContext();
        var command = context.BuildValidCommand() with
        {
            TicketId = Guid.NewGuid()
        };
        context.TicketRepositoryMock
            .Setup(x => x.GetAsync(command.TicketId!.Value))
            .ReturnsAsync((Ticket?)null);

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.InvalidLink);
    }

    /// <summary>
    /// <c>projectId</c> from another company fails with the same code.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProjectIsInAnotherCompany_ShouldReturnInvalidLink()
    {
        var context = new CreateCalendarEventTestContext();
        var command = context.BuildValidCommand() with
        {
            ProjectId = Guid.NewGuid()
        };
        context.ProjectRepositoryMock
            .Setup(x => x.GetAsync(command.ProjectId!.Value))
            .ReturnsAsync((Project?)null);

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.InvalidLink);
    }

    /// <summary>
    /// A <c>Created</c> log row is written exactly once with the new activity id and the resolved status id.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCreating_ShouldWriteCreatedLogRow()
    {
        var context = new CreateCalendarEventTestContext();
        CalendarEventLog? captured = null;
        context.CalendarEventLogRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CalendarEventLog>()))
            .Callback<CalendarEventLog>(log => captured = log)
            .ReturnsAsync(true);

        var response = await context.CreateHandler().Handle(context.BuildValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Action.Should().Be(CalendarEventLogAction.Created);
        captured.NewStatusCompanyId.Should().Be(context.ActivityCompany.InitialCalendarStatusCompanyId);
        captured.CalendarEventId.Should().Be(context.InsertedEvent!.Id);
    }

    /// <summary>
    /// The DTO is mapped from the inserted row, including the origin channel (<c>WEB</c>) and origin type
    /// (<c>User</c>), and the company's <c>RescheduledByOthersStatusCompanyId</c> is NOT applied at create time.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCreating_ShouldSetOriginToWebAndKeepCreatedStatus()
    {
        var context = new CreateCalendarEventTestContext();

        var response = await context.CreateHandler().Handle(context.BuildValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Origin.ChannelCode.Should().Be(CommunicationChannelCodes.Web);
        response.Data.Origin.Type.Should().Be(CalendarEventOriginType.User.ToString());
        response.Data.Status.Id.Should().NotBe(context.CalendarCompany.RescheduledByOthersStatusCompanyId);
        context.InsertedEvent!.OwnerUserId.Should().Be(context.ActorUserId);
    }

    /// <summary>
    /// When the company has no calendar configuration the handler answers
    /// <c>CALENDAR_COMPANY_NOT_CONFIGURED</c> without inserting anything.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIsNotConfigured_ShouldReturnCompanyNotConfigured()
    {
        var context = new CreateCalendarEventTestContext();
        context.CalendarCompanyRepositoryMock.SetupRows(Array.Empty<CalendarCompany>());

        var response = await context.CreateHandler().Handle(context.BuildValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
        context.CalendarEventsRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    /// <summary>
    /// Reusable mocks for the create handler tests. Sets up the company, calendar, activity, status, and the
    /// web channel.
    /// </summary>
    private sealed class CreateCalendarEventTestContext
    {
        public CreateCalendarEventTestContext()
        {
            CompanyId = Guid.NewGuid();
            ActorUserId = Guid.NewGuid();
            CalendarId = Guid.NewGuid();
            ActivityCompanyId = Guid.NewGuid();
            InitialStatusId = Guid.NewGuid();
            RescheduledStatusId = Guid.NewGuid();
            GlobalStatusId = Guid.NewGuid();
            GlobalActivityId = Guid.NewGuid();
            WebChannelId = Guid.NewGuid();

            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(ActorUserId.ToString());

            CalendarCompany = new CalendarCompany
            {
                CompanyId = CompanyId,
                TimeZone = TimeZoneId,
                IsOpen24Hours = true,
                DefaultActivityUserId = ActorUserId,
                RescheduledByOthersStatusCompanyId = RescheduledStatusId,
                DefaultCalendarName = "Mi calendario"
            };
            CalendarCompanyRepositoryMock.SetupRows(new[] { CalendarCompany });

            UserCalendar = new UserCalendar
            {
                CompanyId = CompanyId,
                OwnerUserId = ActorUserId,
                Name = "Mi calendario",
                Color = "#1E88E5",
                IsActive = true
            };
            UserCalendarRepositoryMock.Setup(x => x.GetAsync(CalendarId)).ReturnsAsync(UserCalendar);

            CalendarUserConfiguration configuration = new()
            {
                CompanyId = CompanyId,
                UserId = ActorUserId,
                DefaultCalendarId = CalendarId,
                WorkDays = CalendarWorkDays.Weekdays
            };
            CalendarUserConfigurationRepositoryMock.SetupRows(new[] { configuration });

            ActivityCompany = new CalendarActivityCompany
            {
                CompanyId = CompanyId,
                CalendarActivityId = GlobalActivityId,
                Name = "Reunión",
                Color = "#1E88E5",
                DefaultDurationMinutes = 60,
                AllowsOverlap = false,
                RequiresBusinessHours = false,
                InitialCalendarStatusCompanyId = InitialStatusId,
                IsActive = true
            };
            ActivityCompanyRepositoryMock.Setup(x => x.GetAsync(ActivityCompanyId)).ReturnsAsync(ActivityCompany);

            CalendarActivity globalActivity = new()
            {
                Code = CalendarCodes.Activities.Meeting,
                Name = "Reunión",
                Color = "#1E88E5",
                DefaultDurationMinutes = 60,
                AllowsOverlap = false,
                RequiresBusinessHours = false,
                IsActive = true
            };
            EntityIdSetter.SetId(globalActivity, GlobalActivityId);
            CalendarActivityRepositoryMock.Setup(x => x.GetAsync(GlobalActivityId)).ReturnsAsync(globalActivity);

            InitialStatus = new CalendarStatusCompany
            {
                CompanyId = CompanyId,
                CalendarStatusId = GlobalStatusId,
                Name = "Pendiente",
                Color = "#FB8C00",
                IsActive = true
            };
            EntityIdSetter.SetId(InitialStatus, InitialStatusId);
            CalendarStatusCompanyRepositoryMock.Setup(x => x.GetAsync(InitialStatusId)).ReturnsAsync(InitialStatus);

            CalendarStatus globalStatus = new()
            {
                Code = CalendarCodes.Statuses.Pending,
                Name = "Pendiente",
                Color = "#FB8C00",
                BlocksTime = true,
                IsActive = true
            };
            EntityIdSetter.SetId(globalStatus, GlobalStatusId);
            CalendarStatusRepositoryMock.Setup(x => x.GetAsync(GlobalStatusId)).ReturnsAsync(globalStatus);

            WebChannel = new CommunicationChannel
            {
                Code = CommunicationChannelCodes.Web,
                Name = "Web",
                IsActive = true
            };
            EntityIdSetter.SetId(WebChannel, WebChannelId);
            CommunicationChannelRepositoryMock.SetupRows(new[] { WebChannel });
            // BuildResultAsync looks up the origin channel by id after the row is inserted; SetupRows only configures
            // the predicate-based read, so the GetAsync path returns null without this setup.
            CommunicationChannelRepositoryMock.Setup(x => x.GetAsync(WebChannelId)).ReturnsAsync(WebChannel);

            ApplicationUser actor = new()
            {
                Id = ActorUserId,
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = "ada@x.com"
            };
            ApplicationUserRepositoryMock.Setup(x => x.GetAsync(ActorUserId)).ReturnsAsync(actor);

            CalendarEventsRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<CalendarEvent>()))
                .Callback<CalendarEvent>(e => InsertedEvent = e)
                .ReturnsAsync(true);

            CalendarEventLogRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<CalendarEventLog>())).ReturnsAsync(true);

            UnitOfWorkMock.Setup(x => x.CalendarEvents).Returns(CalendarEventsRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CommunicationChannel>()).Returns(CommunicationChannelRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarCompany>()).Returns(CalendarCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarUserConfiguration>()).Returns(CalendarUserConfigurationRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<UserCalendar>()).Returns(UserCalendarRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivityCompany>()).Returns(ActivityCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivity>()).Returns(CalendarActivityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatusCompany>()).Returns(CalendarStatusCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>()).Returns(CalendarStatusRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationUser>()).Returns(ApplicationUserRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(PersonRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Customer>()).Returns(CustomerRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Ticket>()).Returns(TicketRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Project>()).Returns(ProjectRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<UserCompany>()).Returns(UserCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarEventLog>()).Returns(CalendarEventLogRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            UnitOfWorkMock
                .Setup(x => x.CalendarEvents.GetScheduleRowsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CalendarEventScheduleRow>());
            UnitOfWorkMock
                .Setup(x => x.AcquireScheduleLockAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        public Guid CompanyId { get; }
        public Guid ActorUserId { get; }
        public Guid CalendarId { get; }
        public Guid ActivityCompanyId { get; }
        public Guid InitialStatusId { get; }
        public Guid RescheduledStatusId { get; }
        public Guid GlobalStatusId { get; }
        public Guid GlobalActivityId { get; }
        public Guid WebChannelId { get; }

        public CalendarCompany CalendarCompany { get; }
        public UserCalendar UserCalendar { get; }
        public CalendarActivityCompany ActivityCompany { get; }
        public CalendarStatusCompany InitialStatus { get; }
        public CommunicationChannel WebChannel { get; }

        public CalendarEvent? InsertedEvent { get; private set; }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICalendarEventRepository> CalendarEventsRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CommunicationChannel>> CommunicationChannelRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarCompany>> CalendarCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarUserConfiguration>> CalendarUserConfigurationRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserCalendar>> UserCalendarRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarActivityCompany>> ActivityCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarActivity>> CalendarActivityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatusCompany>> CalendarStatusCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatus>> CalendarStatusRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> ApplicationUserRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Customer>> CustomerRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Project>> ProjectRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserCompany>> UserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarEventLog>> CalendarEventLogRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ICalendarRecurrenceExpander> ExpanderMock { get; } = new();
        public CreateCalendarEventCommandHandler CreateHandler()
        {
        ExpanderMock.Setup(x => x.ValidateRule(It.IsAny<string>())).Returns((string r) => CalendarRecurrenceRule.Parse(r));
            var companyGuard = new CalendarCompanyGuard(UnitOfWorkMock.Object);
            var catalogReader = new CalendarCatalogReader(UnitOfWorkMock.Object);
            var initialStatusResolver = new CalendarEventInitialStatusResolver(UnitOfWorkMock.Object, catalogReader);
            var scheduleGuard = new CalendarEventScheduleGuard(UnitOfWorkMock.Object, ExpanderMock.Object);
            var logWriter = new CalendarEventLogWriter(UnitOfWorkMock.Object);
            var processor = new CalendarEventCommandProcessor(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                companyGuard,
                catalogReader,
                initialStatusResolver,
                scheduleGuard,
                logWriter,
                ExpanderMock.Object);
            return new CreateCalendarEventCommandHandler(processor);
        }

        public CreateCalendarEventCommand BuildValidCommand()
        {
            var start = new DateTimeOffset(2026, 6, 15, 14, 0, 0, TimeSpan.Zero);
            return new CreateCalendarEventCommand
            {
                CalendarId = CalendarId,
                CalendarActivityCompanyId = ActivityCompanyId,
                Title = "Sync",
                Summary = "Daily",
                Start = start,
                End = start.AddMinutes(60),
                IsAllDay = false
            };
        }
    }
}
