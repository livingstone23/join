using JOIN.Domain.Common;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.DeleteCalendarPanelEvent;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Admin;
using JOIN.Application.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarPanel.Commands.DeleteCalendarPanelEvent;

/// <summary>
/// SPEC 44 F10 / CalendarPanel group — delete handler drives <see cref="CalendarEventCommandProcessor.DeleteAsync"/>
/// with <c>panel = true</c>, which removes the activity from the cross-user views (R8) while preserving the
/// tenant boundary (a foreign-company activity answers 404). The change-status branch of the same processor
/// is exercised in the CalendarEvents group.
/// </summary>
public sealed class DeleteCalendarPanelEventCommandHandlerTests
{
    private const string TimeZoneId = "UTC";

    /// <summary>
    /// Soft-deleting a non-recurring activity of another user of the company succeeds, removes the row
    /// from the views and writes a <c>Deleted</c> log entry.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDeletingOwnersActivity_ShouldMarkAsDeletedAndLog()
    {
        var ctx = new DeleteCalendarPanelEventTestContext();
        ctx.Master.OwnerUserId = Guid.NewGuid();

        var response = await ctx.CreateHandler().Handle(new DeleteCalendarPanelEventCommand(ctx.MasterId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeTrue();
        ctx.DeletedIds.Should().Contain(ctx.MasterId);
        ctx.CapturedLogs.Should().Contain(l => l.Action == CalendarEventLogAction.Deleted);
    }

    /// <summary>
    /// Missing calendar configuration yields <c>CALENDAR_COMPANY_NOT_CONFIGURED</c>; nothing is removed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIsNotConfigured_ShouldReturnCompanyNotConfiguredError()
    {
        var ctx = new DeleteCalendarPanelEventTestContext();
        ctx.CalendarCompanyRepositoryMock.SetupRows(Array.Empty<CalendarCompany>());

        var response = await ctx.CreateHandler().Handle(new DeleteCalendarPanelEventCommand(ctx.MasterId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
        ctx.CalendarEventsRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    /// <summary>
    /// A foreign-company activity answers <c>CALENDAR_EVENT_NOT_FOUND</c> with the panel flag — the
    /// processor never discloses that the row exists in another tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEventIsInAnotherCompany_ShouldReturnEventNotFoundError()
    {
        var ctx = new DeleteCalendarPanelEventTestContext();
        ctx.Master.CompanyId = Guid.NewGuid();

        var response = await ctx.CreateHandler().Handle(new DeleteCalendarPanelEventCommand(ctx.MasterId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventNotFound);
    }

    /// <summary>
    /// Reusable mocks for the panel-delete handler tests. Seeds a non-recurring activity owned by someone
    /// other than the actor so the panel-path ownership branches exercise both ways.
    /// </summary>
    private sealed class DeleteCalendarPanelEventTestContext
    {
        public DeleteCalendarPanelEventTestContext()
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
                IsActive = true
            };
            UserCalendarRepositoryMock.Setup(x => x.GetAsync(CalendarId)).ReturnsAsync(UserCalendar);

            ActivityCompany = new CalendarActivityCompany
            {
                CompanyId = CompanyId,
                CalendarActivityId = GlobalActivityId,
                Name = "Meeting",
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

            WebChannel = new CommunicationChannel
            {
                Code = CommunicationChannelCodes.Web,
                Name = "Web",
                IsActive = true
            };
            EntityIdSetter.SetId(WebChannel, WebChannelId);
            CommunicationChannelRepositoryMock.SetupRows(new[] { WebChannel });

            Master = new CalendarEvent
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
            EntityIdSetter.SetId(Master, MasterId);
            CalendarEventsRepositoryMock.Setup(x => x.GetAsync(MasterId)).ReturnsAsync(Master);
            CalendarEventsRepositoryMock
                .Setup(x => x.UpdateAsync(It.IsAny<CalendarEvent>()))
                .Callback<CalendarEvent>(e =>
                {
                    if (e.GcRecord != 0)
                    {
                        DeletedIds.Add(e.Id);
                    }
                })
                .ReturnsAsync(true);
            CalendarEventsRepositoryMock
                .Setup(x => x.GetExceptionsAsync(MasterId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CalendarEvent>());

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

        public List<Guid> DeletedIds { get; } = new();
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
        public DeleteCalendarPanelEventCommandHandler CreateHandler()
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
            return new DeleteCalendarPanelEventCommandHandler(processor);
        }
    }
}
