using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.CalendarPanel;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.ChangeCalendarPanelEventStatus;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Domain.Calendars;
using JOIN.Domain.Admin;
using JOIN.Application.Common;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Calendars.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarPanel.Commands.ChangeCalendarPanelEventStatus;

/// <summary>
/// SPEC 44 panel — change-status handler first asks <see cref="CalendarPanelAccess.RequireOthersUpdateAsync"/>
/// which enforces <c>CalendarOthers.CanUpdate</c> (SuperAdmins bypass). Only when that gate passes does it
/// forward to <see cref="CalendarEventCommandProcessor.ChangeStatusAsync"/>, which itself enforces the
/// cancellation-notes rule (R6: cancelling needs a reason).
/// </summary>
public sealed class ChangeCalendarPanelEventStatusCommandHandlerTests
{
    private const string TimeZoneId = "UTC";

    /// <summary>
    /// A SuperAdmin role short-circuits the permission check before the panel reaches for the
    /// <see cref="IPermissionService"/>; the handler then forwards to the processor.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActorIsSuperAdmin_ShouldBypassPermissionCheck()
    {
        var ctx = new ChangeCalendarPanelEventStatusTestContext();
        ctx.CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(true);

        var response = await ctx.CreateHandler().Handle(ctx.BuildValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        ctx.PermissionServiceMock.Verify(
            x => x.HasPermissionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PermissionFlags?>()),
            Times.Never);
    }

    /// <summary>
    /// Non-SuperAdmin with <c>HasPermissionAsync(CalendarOthers, CanUpdate)</c> returning true passes the gate
    /// and calls the processor with <c>panel = true</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPermissionGranted_ShouldCallProcessor()
    {
        var ctx = new ChangeCalendarPanelEventStatusTestContext();
        ctx.CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(ctx.ActorUserId.ToString());
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        ctx.PermissionServiceMock
            .Setup(x => x.HasPermissionAsync(ctx.ActorUserId.ToString(), ctx.CompanyId.ToString(),
                CalendarPanelAccess.OthersResource, "PATCH", PermissionFlags.CanUpdate))
            .ReturnsAsync(true);

        var response = await ctx.CreateHandler().Handle(ctx.BuildValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        ctx.PermissionServiceMock.Verify(
            x => x.HasPermissionAsync(ctx.ActorUserId.ToString(), ctx.CompanyId.ToString(),
                CalendarPanelAccess.OthersResource, "PATCH", PermissionFlags.CanUpdate),
            Times.Once);
    }

    /// <summary>
    /// When the permission service returns false the handler aborts with
    /// <c>CALENDAR_OTHERS_PERMISSION_REQUIRED</c> without invoking the processor.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPermissionDenied_ShouldReturnPermissionRequiredError()
    {
        var ctx = new ChangeCalendarPanelEventStatusTestContext();
        ctx.CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(ctx.ActorUserId.ToString());
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ctx.CompanyId);
        ctx.PermissionServiceMock
            .Setup(x => x.HasPermissionAsync(It.IsAny<string>(), It.IsAny<string>(),
                CalendarPanelAccess.OthersResource, "PATCH", PermissionFlags.CanUpdate))
            .ReturnsAsync(false);

        var response = await ctx.CreateHandler().Handle(ctx.BuildValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.OthersPermissionRequired);
        ctx.CalendarEventsRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    /// <summary>
    /// Cancelling (status = <c>CalendarCodes.Statuses.Cancelled</c>) without notes is rejected by the
    /// processor with <c>CALENDAR_CANCELLATION_REASON_REQUIRED</c>; the permission gate has already passed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCancellingWithoutNotes_ShouldReturnCancellationReasonRequiredError()
    {
        var ctx = new ChangeCalendarPanelEventStatusTestContext();
        ctx.CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(true);
        var cancelledStatus = new CalendarStatusCompany
        {
            CompanyId = ctx.CompanyId,
            CalendarStatusId = ctx.GlobalStatusId,
            Name = "Cancelled",
            IsActive = true
        };
        ctx.CalendarStatusCompanyRepositoryMock.Setup(x => x.GetAsync(ctx.CancelledStatusId)).ReturnsAsync(cancelledStatus);
        var globalCancelled = new CalendarStatus
        {
            Code = CalendarCodes.Statuses.Cancelled,
            Name = "Cancelled",
            BlocksTime = false,
            IsFinal = true,
            IsActive = true
        };
        EntityIdSetter.SetId(globalCancelled, ctx.GlobalStatusId);
        ctx.CalendarStatusRepositoryMock.Setup(x => x.GetAsync(ctx.GlobalStatusId)).ReturnsAsync(globalCancelled);

        var command = ctx.BuildValidCommand() with { CalendarStatusCompanyId = ctx.CancelledStatusId, Notes = null };

        var response = await ctx.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CancellationReasonRequired);
    }

    /// <summary>
    /// Reusable mocks for the change-status handler tests.
    /// </summary>
    private sealed class ChangeCalendarPanelEventStatusTestContext
    {
        public ChangeCalendarPanelEventStatusTestContext()
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
            CancelledStatusId = Guid.NewGuid();
            TargetStatusId = Guid.NewGuid();
            EventId = Guid.NewGuid();
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

            TargetStatus = new CalendarStatusCompany
            {
                CompanyId = CompanyId,
                CalendarStatusId = GlobalStatusId,
                Name = "Confirmed",
                IsActive = true
            };
            CalendarStatusCompanyRepositoryMock.Setup(x => x.GetAsync(TargetStatusId)).ReturnsAsync(TargetStatus);

            WebChannel = new CommunicationChannel
            {
                Code = CommunicationChannelCodes.Web,
                Name = "Web",
                IsActive = true
            };
            EntityIdSetter.SetId(WebChannel, WebChannelId);
            CommunicationChannelRepositoryMock.SetupRows(new[] { WebChannel });

            ApplicationUser owner = new()
            {
                Id = OwnerUserId,
                FirstName = "Owner",
                LastName = "Doe",
                Email = "owner@x.com"
            };
            ApplicationUserRepositoryMock.Setup(x => x.GetAsync(OwnerUserId)).ReturnsAsync(owner);

            Event = new CalendarEvent
            {
                CompanyId = CompanyId,
                CalendarId = CalendarId,
                OwnerUserId = OwnerUserId,
                CalendarActivityCompanyId = ActivityCompanyId,
                CalendarStatusCompanyId = InitialStatusId,
                Title = "Activity",
                StartUtc = MasterStartUtc,
                EndUtc = MasterEndUtc,
                IsAllDay = false,
                OriginChannelId = WebChannelId
            };
            EntityIdSetter.SetId(Event, EventId);
            CalendarEventsRepositoryMock.Setup(x => x.GetAsync(EventId)).ReturnsAsync(Event);
            CalendarEventsRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<CalendarEvent>())).ReturnsAsync(true);
            CalendarEventLogRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<CalendarEventLog>())).ReturnsAsync(true);

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
        public Guid CancelledStatusId { get; }
        public Guid TargetStatusId { get; }
        public Guid EventId { get; }
        public DateTime MasterStartUtc { get; }
        public DateTime MasterEndUtc { get; }

        public CalendarCompany Company { get; }
        public UserCalendar UserCalendar { get; }
        public CalendarActivityCompany ActivityCompany { get; }
        public CalendarStatusCompany InitialStatus { get; }
        public CalendarStatusCompany TargetStatus { get; }
        public CommunicationChannel WebChannel { get; }
        public CalendarEvent Event { get; }

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
        public Mock<IPermissionService> PermissionServiceMock { get; } = new();

        public ChangeCalendarPanelEventStatusCommandHandler CreateHandler()
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
            var access = new CalendarPanelAccess(PermissionServiceMock.Object, CurrentUserServiceMock.Object);
            return new ChangeCalendarPanelEventStatusCommandHandler(processor, access);
        }

        public ChangeCalendarPanelEventStatusCommand BuildValidCommand() =>
            new()
            {
                Id = EventId,
                CalendarStatusCompanyId = TargetStatusId,
                Notes = "Approved"
            };
    }
}
