using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.CreateCalendarPanelEvent;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Admin;
using JOIN.Application.Common;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarPanel.Commands.CreateCalendarPanelEvent;

/// <summary>
/// SPEC 44 F10 / CalendarPanel group — create handler delegates to <see cref="CalendarEventCommandProcessor"/>
/// with the panel owner (<c>request.OwnerUserId</c>) as the second argument so R5 (business hours) applies to
/// non-self creations. The same initial-status rule as <c>CalendarEvents</c> drives the activity's first status:
/// <see cref="CalendarActivityCompany.InitialCalendarStatusCompanyId"/>, never anything from the request body.
/// </summary>
public sealed class CreateCalendarPanelEventCommandHandlerTests
{
    private const string TimeZoneId = "UTC";

    /// <summary>
    /// The panel create sets the initial status of the activity from
    /// <see cref="CalendarActivityCompany.InitialCalendarStatusCompanyId"/>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPanelUserIsConfigured_ShouldUseInitialStatusFromActivityType()
    {
        var ctx = new CreateCalendarPanelEventTestContext();
        var command = ctx.BuildValidCommand();

        var response = await ctx.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Status.Id.Should().Be(ctx.ActivityCompany.InitialCalendarStatusCompanyId);
        ctx.InsertedEvent!.OwnerUserId.Should().Be(command.OwnerUserId);
    }

    /// <summary>
    /// When the configured owner of the panel event is not an active member of the company the handler
    /// answers <c>CALENDAR_USER_NOT_IN_COMPANY</c>; nothing is inserted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOwnerIsNotInCompany_ShouldReturnUserNotInCompanyError()
    {
        var ctx = new CreateCalendarPanelEventTestContext();
        var command = ctx.BuildValidCommand();
        ctx.UserCompanyRepositoryMock.SetupRows(Array.Empty<UserCompany>());

        var response = await ctx.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.UserNotInCompany);
        ctx.CalendarEventsRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    /// <summary>
    /// Without <c>CalendarCompany</c> for the tenant the handler answers <c>CALENDAR_COMPANY_NOT_CONFIGURED</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIsNotConfigured_ShouldReturnCompanyNotConfiguredError()
    {
        var ctx = new CreateCalendarPanelEventTestContext();
        ctx.CalendarCompanyRepositoryMock.SetupRows(Array.Empty<CalendarCompany>());

        var response = await ctx.CreateHandler().Handle(ctx.BuildValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
    }

    /// <summary>
    /// Reusable mocks for the panel-create handler tests.
    /// </summary>
    private sealed class CreateCalendarPanelEventTestContext
    {
        public CreateCalendarPanelEventTestContext()
        {
            CompanyId = Guid.NewGuid();
            ActorUserId = Guid.NewGuid();
            OwnerUserId = Guid.NewGuid();
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
                DefaultCalendarName = "Panel calendar"
            };
            CalendarCompanyRepositoryMock.SetupRows(new[] { CalendarCompany });

            UserCalendar = new UserCalendar
            {
                CompanyId = CompanyId,
                OwnerUserId = OwnerUserId,
                Name = "Owner calendar",
                Color = "#1E88E5",
                IsActive = true
            };
            UserCalendarRepositoryMock.Setup(x => x.GetAsync(CalendarId)).ReturnsAsync(UserCalendar);

            CalendarUserConfiguration configuration = new()
            {
                CompanyId = CompanyId,
                UserId = OwnerUserId,
                DefaultCalendarId = CalendarId,
                WorkDays = CalendarWorkDays.Weekdays
            };
            CalendarUserConfigurationRepositoryMock.SetupRows(new[] { configuration });

            ActivityCompany = new CalendarActivityCompany
            {
                CompanyId = CompanyId,
                CalendarActivityId = GlobalActivityId,
                Name = "Meeting",
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
                Name = "Meeting",
                DefaultDurationMinutes = 60,
                IsActive = true
            };
            EntityIdSetter.SetId(globalActivity, GlobalActivityId);
            CalendarActivityRepositoryMock.Setup(x => x.GetAsync(GlobalActivityId)).ReturnsAsync(globalActivity);

            InitialStatus = new CalendarStatusCompany
            {
                CompanyId = CompanyId,
                CalendarStatusId = GlobalStatusId,
                Name = "Pending",
                Color = "#FB8C00",
                IsActive = true
            };
            EntityIdSetter.SetId(InitialStatus, InitialStatusId);
            CalendarStatusCompanyRepositoryMock.Setup(x => x.GetAsync(InitialStatusId)).ReturnsAsync(InitialStatus);

            CalendarStatus globalStatus = new()
            {
                Code = CalendarCodes.Statuses.Pending,
                Name = "Pending",
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
            // BuildResultAsync looks the origin channel up by id after the row is inserted; SetupRows only
            // configures the predicate-based read, so the GetAsync path returns null without this setup.
            CommunicationChannelRepositoryMock.Setup(x => x.GetAsync(WebChannelId)).ReturnsAsync(WebChannel);

            ApplicationUser owner = new()
            {
                Id = OwnerUserId,
                FirstName = "Owner",
                LastName = "Doe",
                Email = "owner@x.com"
            };
            ApplicationUserRepositoryMock.Setup(x => x.GetAsync(OwnerUserId)).ReturnsAsync(owner);

            // The user is a member of the company by default; the cross-company test overrides this to empty.
            UserCompanyRepositoryMock.SetupRows(new[]
            {
                new UserCompany { UserId = OwnerUserId, CompanyId = CompanyId }
            });

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
        public Guid OwnerUserId { get; }
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
        public Mock<IGenericRepository<UserCompany>> UserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarEventLog>> CalendarEventLogRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ICalendarRecurrenceExpander> ExpanderMock { get; } = new();
        public CreateCalendarPanelEventCommandHandler CreateHandler()
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
            return new CreateCalendarPanelEventCommandHandler(processor);
        }

        public CreateCalendarPanelEventCommand BuildValidCommand()
        {
            var start = new DateTimeOffset(2026, 6, 15, 14, 0, 0, TimeSpan.Zero);
            return new CreateCalendarPanelEventCommand
            {
                OwnerUserId = OwnerUserId,
                CalendarId = CalendarId,
                CalendarActivityCompanyId = ActivityCompanyId,
                Title = "Panel sync",
                Summary = "Daily",
                Start = start,
                End = start.AddMinutes(60),
                IsAllDay = false
            };
        }
    }
}
