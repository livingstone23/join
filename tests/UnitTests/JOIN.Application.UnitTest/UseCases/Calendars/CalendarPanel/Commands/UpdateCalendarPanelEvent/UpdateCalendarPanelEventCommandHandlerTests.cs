using JOIN.Domain.Common;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.UpdateCalendarPanelEvent;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Domain.Calendars;
using JOIN.Domain.Admin;
using JOIN.Application.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Calendars.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarPanel.Commands.UpdateCalendarPanelEvent;

/// <summary>
/// SPEC 44 F10 / CalendarPanel group — update handler drives <see cref="CalendarEventCommandProcessor"/> with
/// <c>panel = true</c>, which lets any user of the company open the activity (R2 bypass). R6 then promotes the
/// status to <see cref="CalendarCompany.RescheduledByOthersStatusCompanyId"/> whenever a non-owner changes the
/// time, while a non-time edit by anyone (owner or panel) leaves the status untouched.
/// </summary>
public sealed class UpdateCalendarPanelEventCommandHandlerTests
{
    private const string TimeZoneId = "UTC";

    /// <summary>
    /// When the actor of the panel update is not the owner of the activity and the times change,
    /// the processor keeps the reschedule status of the company on the activity row.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRescheduledByNonOwner_ShouldSetRescheduleStatus()
    {
        var ctx = new UpdateCalendarPanelEventTestContext();
        var rescheduleStatus = new CalendarStatusCompany
        {
            CompanyId = ctx.CompanyId,
            CalendarStatusId = ctx.GlobalStatusId,
            Name = "Rescheduled",
            Color = "#FB8C00",
            IsActive = true
        };
        // The processor writes rescheduleStatus.Id into the event row; without a fixed id, the new
        // status would differ from ctx.Company.RescheduledByOthersStatusCompanyId and the assertion fails.
        EntityIdSetter.SetId(rescheduleStatus, ctx.Company.RescheduledByOthersStatusCompanyId);
        ctx.CalendarStatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(ctx.Company.RescheduledByOthersStatusCompanyId))
            .ReturnsAsync(rescheduleStatus);
        var globalReschedule = new CalendarStatus
        {
            Code = CalendarCodes.Statuses.Rescheduled,
            Name = "Rescheduled",
            BlocksTime = true,
            IsActive = true
        };
        EntityIdSetter.SetId(globalReschedule, ctx.GlobalStatusId);
        ctx.CalendarStatusRepositoryMock.Setup(x => x.GetAsync(ctx.GlobalStatusId)).ReturnsAsync(globalReschedule);

        var newStart = ctx.MasterStartUtc.AddHours(2);
        var command = ctx.BuildValidCommand() with
        {
            Start = new DateTimeOffset(newStart, TimeSpan.Zero),
            End = new DateTimeOffset(newStart.AddHours(1), TimeSpan.Zero)
        };

        var response = await ctx.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message + " " + string.Join(" ", response.Errors ?? []));
        var updated = ctx.UpdatedMasters.Last();
        updated.CalendarStatusCompanyId.Should().Be(ctx.Company.RescheduledByOthersStatusCompanyId);
        ctx.CapturedLogs.Should().Contain(l => l.Action == CalendarEventLogAction.Rescheduled);
    }

    /// <summary>
    /// When only non-time fields change (panel actor regardless of ownership), the status is not touched.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOnlyNonTimeFieldsChange_ShouldKeepOriginalStatus()
    {
        var ctx = new UpdateCalendarPanelEventTestContext();
        var originalStatusId = ctx.Master.CalendarStatusCompanyId;

        var command = ctx.BuildValidCommand() with
        {
            Title = "Renamed",
            Location = "Boardroom"
        };

        var response = await ctx.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message + " " + string.Join(" ", response.Errors ?? []));
        var updated = ctx.UpdatedMasters.Last();
        updated.CalendarStatusCompanyId.Should().Be(originalStatusId);
        updated.Title.Should().Be("Renamed");
    }

    /// <summary>
    /// Missing calendar configuration yields <c>CALENDAR_COMPANY_NOT_CONFIGURED</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIsNotConfigured_ShouldReturnCompanyNotConfiguredError()
    {
        var ctx = new UpdateCalendarPanelEventTestContext();
        ctx.CalendarCompanyRepositoryMock.SetupRows(Array.Empty<CalendarCompany>());

        var response = await ctx.CreateHandler().Handle(ctx.BuildValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
    }

    /// <summary>
    /// Reusable mocks for the panel-update handler tests. Seeds a one-time activity owned by someone
    /// other than the actor, so panel ownership paths are exercised by default.
    /// </summary>
    private sealed class UpdateCalendarPanelEventTestContext
    {
        public UpdateCalendarPanelEventTestContext()
        {
            // Defaults for the real processor flow (persist, then validate): no exceptions, no competing rows and
            // no extra occurrences unless a test sets them up (later setups win in Moq).
            CalendarEventsRepositoryMock
                .Setup(x => x.GetExceptionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<CalendarEvent>());
            CalendarEventsRepositoryMock
                .Setup(x => x.GetScheduleRowsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<JOIN.Application.Interface.Persistence.Calendars.CalendarEventScheduleRow>());
            ExpanderMock
                .Setup(x => x.Expand(It.IsAny<CalendarRecurrenceSeries>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>()))
                .Returns(Array.Empty<CalendarOccurrence>());

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
            MasterId = Guid.NewGuid();
            MasterStartUtc = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 14, 0, 0), DateTimeKind.Utc);
            MasterEndUtc = MasterStartUtc.AddHours(1);

            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(ActorUserId.ToString());

            Company = new CalendarCompany
            {
                CompanyId = CompanyId,
                TimeZone = TimeZoneId,
                IsOpen24Hours = true,
                DefaultActivityUserId = ActorUserId,
                RescheduledByOthersStatusCompanyId = RescheduledStatusId,
                DefaultCalendarName = "Panel"
            };
            CalendarCompanyRepositoryMock.SetupRows(new[] { Company });

            UserCalendar = new UserCalendar
            {
                CompanyId = CompanyId,
                OwnerUserId = OwnerUserId,
                Name = "Owner",
                Color = "#1E88E5",
                IsActive = true
            };
            UserCalendarRepositoryMock.Setup(x => x.GetAsync(CalendarId)).ReturnsAsync(UserCalendar);

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
            // BuildResultAsync (called from UpdateAsync / SaveValidateAndBuildAsync) looks the channel up by id;
            // the SetupRows helper above only configures the predicate-based read, leaving GetAsync unmocked.
            CommunicationChannelRepositoryMock.Setup(x => x.GetAsync(WebChannelId)).ReturnsAsync(WebChannel);

            Master = new CalendarEvent
            {
                CompanyId = CompanyId,
                CalendarId = CalendarId,
                OwnerUserId = OwnerUserId,
                CalendarActivityCompanyId = ActivityCompanyId,
                CalendarStatusCompanyId = InitialStatusId,
                Title = "Initial",
                StartUtc = MasterStartUtc,
                EndUtc = MasterEndUtc,
                IsAllDay = false,
                OriginChannelId = WebChannelId
            };
            EntityIdSetter.SetId(Master, MasterId);
            CalendarEventsRepositoryMock.Setup(x => x.GetAsync(MasterId)).ReturnsAsync(Master);
            CalendarEventsRepositoryMock
                .Setup(x => x.UpdateAsync(It.IsAny<CalendarEvent>()))
                .Callback<CalendarEvent>(e => UpdatedMasters.Add(e))
                .ReturnsAsync(true);

            CalendarEventLogRepositoryMock
                .Setup(x => x.InsertAsync(It.IsAny<CalendarEventLog>()))
                .Callback<CalendarEventLog>(log => CapturedLogs.Add(log))
                .ReturnsAsync(true);

            UnitOfWorkMock.Setup(x => x.CalendarEvents).Returns(CalendarEventsRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CommunicationChannel>()).Returns(CommunicationChannelRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarCompany>()).Returns(CalendarCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<UserCalendar>()).Returns(UserCalendarRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivityCompany>()).Returns(ActivityCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarActivity>()).Returns(CalendarActivityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatusCompany>()).Returns(CalendarStatusCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarStatus>()).Returns(CalendarStatusRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationUser>()).Returns(ApplicationUserRepositoryMock.Object);
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
        public Guid MasterId { get; }
        public DateTime MasterStartUtc { get; }
        public DateTime MasterEndUtc { get; }

        public CalendarCompany Company { get; }
        public UserCalendar UserCalendar { get; }
        public CalendarActivityCompany ActivityCompany { get; }
        public CalendarStatusCompany InitialStatus { get; }
        public CommunicationChannel WebChannel { get; }
        public CalendarEvent Master { get; }

        public List<CalendarEvent> UpdatedMasters { get; } = new();
        public List<CalendarEventLog> CapturedLogs { get; } = new();

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICalendarEventRepository> CalendarEventsRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CommunicationChannel>> CommunicationChannelRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarCompany>> CalendarCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserCalendar>> UserCalendarRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarActivityCompany>> ActivityCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarActivity>> CalendarActivityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatusCompany>> CalendarStatusCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarStatus>> CalendarStatusRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> ApplicationUserRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CalendarEventLog>> CalendarEventLogRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ICalendarRecurrenceExpander> ExpanderMock { get; } = new();
        public UpdateCalendarPanelEventCommandHandler CreateHandler()
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
            return new UpdateCalendarPanelEventCommandHandler(processor);
        }

        public UpdateCalendarPanelEventCommand BuildValidCommand() =>
            new()
            {
                Id = MasterId,
                CalendarId = CalendarId,
                CalendarActivityCompanyId = ActivityCompanyId,
                Title = "Edited",
                Start = new DateTimeOffset(MasterStartUtc, TimeSpan.Zero),
                End = new DateTimeOffset(MasterEndUtc, TimeSpan.Zero),
                IsAllDay = false
            };
    }
}
