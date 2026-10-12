using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.ChangeCalendarEventStatus;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEvents.Commands.ChangeCalendarEventStatus;

/// <summary>
/// SPEC 44 F10 / CalendarEvents group — status change handler covers the cancellation-notes rule (R6) and the
/// overlap revalidation when re-activating an activity from <c>CANCELLED</c> to a status that blocks time.
/// </summary>
public sealed class ChangeCalendarEventStatusCommandHandlerTests
{
    private static readonly Fixture _fixture = new();
    private const string TimeZoneId = "America/Managua";

    /// <summary>
    /// Targeting <c>CANCELLED</c> without notes fails with <c>CALENDAR_CANCELLATION_REASON_REQUIRED</c>; nothing
    /// is written and no log row is queued.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCancellingWithoutNotes_ShouldReturnCancellationReasonRequired()
    {
        var context = new ChangeCalendarEventStatusTestContext();
        var cancelledStatus = new CalendarStatusCompany
        {
            CompanyId = context.CompanyId,
            CalendarStatusId = Guid.NewGuid(),
            Name = "Cancelada",
            Color = "#9E9E9E",
            IsActive = true
        };
        context.CalendarStatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(cancelledStatus.Id))
            .ReturnsAsync(cancelledStatus);
        var cancelledGlobal = new CalendarStatus
        {
            Code = CalendarCodes.Statuses.Cancelled,
            Name = "Cancelada",
            Color = "#9E9E9E",
            BlocksTime = false,
            IsActive = true
        };
        EntityIdSetter.SetId(cancelledGlobal, cancelledStatus.CalendarStatusId);
        context.CalendarStatusRepositoryMock
            .Setup(x => x.GetAsync(cancelledStatus.CalendarStatusId))
            .ReturnsAsync(cancelledGlobal);

        var command = context.BuildValidCommand() with
        {
            CalendarStatusCompanyId = cancelledStatus.Id,
            Notes = null
        };

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CancellationReasonRequired);
        context.CalendarEventsRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarEvent>()), Times.Never);
        context.CalendarEventLogRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CalendarEventLog>()), Times.Never);
    }

    /// <summary>
    /// Cancelling WITH notes succeeds, the status changes, and a <c>StatusChanged</c> log row is written with the
    /// reason in the notes.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCancellingWithNotes_ShouldChangeStatusAndLogStatusChanged()
    {
        var context = new ChangeCalendarEventStatusTestContext();
        var cancelledStatus = new CalendarStatusCompany
        {
            CompanyId = context.CompanyId,
            CalendarStatusId = Guid.NewGuid(),
            Name = "Cancelada",
            Color = "#9E9E9E",
            IsActive = true
        };
        context.CalendarStatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(cancelledStatus.Id))
            .ReturnsAsync(cancelledStatus);
        var cancelledGlobal = new CalendarStatus
        {
            Code = CalendarCodes.Statuses.Cancelled,
            Name = "Cancelada",
            Color = "#9E9E9E",
            BlocksTime = false,
            IsActive = true
        };
        EntityIdSetter.SetId(cancelledGlobal, cancelledStatus.CalendarStatusId);
        context.CalendarStatusRepositoryMock
            .Setup(x => x.GetAsync(cancelledStatus.CalendarStatusId))
            .ReturnsAsync(cancelledGlobal);

        var command = context.BuildValidCommand() with
        {
            CalendarStatusCompanyId = cancelledStatus.Id,
            Notes = "Requester cancelled"
        };

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Status.Code.Should().Be(CalendarCodes.Statuses.Cancelled);
        context.CapturedLogs.Should().ContainSingle(l => l.Action == CalendarEventLogAction.StatusChanged);
        context.CapturedLogs.Single().Notes.Should().Be("Requester cancelled");
    }

    /// <summary>
    /// Switching from a non-blocking status (<c>CANCELLED</c>) to a blocking status (<c>CONFIRMED</c>) revalidates
    /// the overlap. The schedule lock is acquired and the row set is fetched, so the revalidation branch is
    /// exercised end-to-end.
    /// </summary>
    [Fact]
    public async Task Handle_WhenReactivatingFromCancelled_ShouldRevalidateOverlap()
    {
        var context = new ChangeCalendarEventStatusTestContext();
        var cancelledStatusCompany = new CalendarStatusCompany
        {
            CompanyId = context.CompanyId,
            CalendarStatusId = context.CancelledGlobalId,
            Name = "Cancelada",
            Color = "#9E9E9E",
            IsActive = true
        };
        EntityIdSetter.SetId(cancelledStatusCompany, Guid.NewGuid());
        var confirmedStatusCompany = new CalendarStatusCompany
        {
            CompanyId = context.CompanyId,
            CalendarStatusId = context.ConfirmedGlobalId,
            Name = "Confirmada",
            Color = "#1E88E5",
            IsActive = true
        };
        EntityIdSetter.SetId(confirmedStatusCompany, Guid.NewGuid());
        context.Master.CalendarStatusCompanyId = cancelledStatusCompany.Id;

        context.CalendarStatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(cancelledStatusCompany.Id))
            .ReturnsAsync(cancelledStatusCompany);
        context.CalendarStatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(confirmedStatusCompany.Id))
            .ReturnsAsync(confirmedStatusCompany);

        var cancelledGlobal = new CalendarStatus
        {
            Code = CalendarCodes.Statuses.Cancelled,
            Name = "Cancelada",
            Color = "#9E9E9E",
            BlocksTime = false,
            IsActive = true
        };
        EntityIdSetter.SetId(cancelledGlobal, context.CancelledGlobalId);
        var confirmedGlobal = new CalendarStatus
        {
            Code = CalendarCodes.Statuses.Confirmed,
            Name = "Confirmada",
            Color = "#1E88E5",
            BlocksTime = true,
            IsActive = true
        };
        EntityIdSetter.SetId(confirmedGlobal, context.ConfirmedGlobalId);
        context.CalendarStatusRepositoryMock
            .Setup(x => x.GetAsync(context.CancelledGlobalId))
            .ReturnsAsync(cancelledGlobal);
        context.CalendarStatusRepositoryMock
            .Setup(x => x.GetAsync(context.ConfirmedGlobalId))
            .ReturnsAsync(confirmedGlobal);

        var command = context.BuildValidCommand() with
        {
            CalendarStatusCompanyId = confirmedStatusCompany.Id
        };

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        // The schedule lock was acquired for the revalidation branch and the response succeeded
        // because the schedule rows have AllowsOverlap = true and therefore do not compete.
        response.IsSuccess.Should().BeTrue();
        context.UnitOfWorkMock.Verify(x => x.AcquireScheduleLockAsync(
            context.CompanyId, context.ActorUserId, It.IsAny<CancellationToken>()), Times.Once);
        response.Data!.Status.Code.Should().Be(CalendarCodes.Statuses.Confirmed);
    }

    /// <summary>
    /// When the target status is not enabled in the company, the response is
    /// <c>CALENDAR_STATUS_NOT_ENABLED</c> and nothing is updated.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStatusNotEnabledInCompany_ShouldReturnStatusNotEnabled()
    {
        var context = new ChangeCalendarEventStatusTestContext();
        // Setup the global status to be a code that exists but the company status is not active.
        var globalId = Guid.NewGuid();
        var statusCompany = new CalendarStatusCompany
        {
            CompanyId = context.CompanyId,
            CalendarStatusId = globalId,
            Name = "Disabled",
            Color = "#000",
            IsActive = false
        };
        context.CalendarStatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(statusCompany.Id))
            .ReturnsAsync(statusCompany);
        var global = new CalendarStatus
        {
            Code = "DISABLED",
            Name = "Disabled",
            Color = "#000",
            BlocksTime = true,
            IsActive = false
        };
        EntityIdSetter.SetId(global, globalId);
        context.CalendarStatusRepositoryMock.Setup(x => x.GetAsync(globalId)).ReturnsAsync(global);

        var command = context.BuildValidCommand() with { CalendarStatusCompanyId = statusCompany.Id };
        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.StatusNotEnabled);
    }

    /// <summary>
    /// Reusable mocks for the status change handler tests. Seeds a one-time event owned by the current user.
    /// </summary>
    private sealed class ChangeCalendarEventStatusTestContext
    {
        public ChangeCalendarEventStatusTestContext()
        {
            CompanyId = Guid.NewGuid();
            ActorUserId = Guid.NewGuid();
            CalendarId = Guid.NewGuid();
            ActivityCompanyId = Guid.NewGuid();
            PendingStatusId = Guid.NewGuid();
            RescheduledStatusId = Guid.NewGuid();
            PendingGlobalId = Guid.NewGuid();
            CancelledGlobalId = Guid.NewGuid();
            ConfirmedGlobalId = Guid.NewGuid();
            WebChannelId = Guid.NewGuid();
            EventId = Guid.NewGuid();
            EventStartUtc = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 14, 0, 0), DateTimeKind.Utc);
            EventEndUtc = EventStartUtc.AddHours(1);

            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(ActorUserId.ToString());

            Company = new CalendarCompany
            {
                CompanyId = CompanyId,
                TimeZone = TimeZoneId,
                IsOpen24Hours = true,
                DefaultActivityUserId = ActorUserId,
                RescheduledByOthersStatusCompanyId = RescheduledStatusId,
                DefaultCalendarName = "Mi calendario"
            };
            CalendarCompanyRepositoryMock.SetupRows(new[] { Company });

            var userCalendar = new UserCalendar
            {
                CompanyId = CompanyId,
                OwnerUserId = ActorUserId,
                Name = "Mi calendario",
                Color = "#1E88E5",
                IsActive = true
            };
            UserCalendarRepositoryMock.Setup(x => x.GetAsync(CalendarId)).ReturnsAsync(userCalendar);

            var activityCompany = new CalendarActivityCompany
            {
                CompanyId = CompanyId,
                CalendarActivityId = Guid.NewGuid(),
                Name = "Reunión",
                Color = "#1E88E5",
                DefaultDurationMinutes = 60,
                AllowsOverlap = false,
                RequiresBusinessHours = false,
                InitialCalendarStatusCompanyId = PendingStatusId,
                IsActive = true
            };
            ActivityCompanyRepositoryMock.Setup(x => x.GetAsync(ActivityCompanyId)).ReturnsAsync(activityCompany);
            var globalActivityForChange = new CalendarActivity
                {
                    Code = CalendarCodes.Activities.Meeting,
                    Name = "Reunión",
                    Color = "#1E88E5",
                    IsActive = true
                };
            EntityIdSetter.SetId(globalActivityForChange, activityCompany.CalendarActivityId);
            CalendarActivityRepositoryMock.Setup(x => x.GetAsync(activityCompany.CalendarActivityId))
                .ReturnsAsync(globalActivityForChange);

            var pendingStatus = new CalendarStatusCompany
            {
                CompanyId = CompanyId,
                CalendarStatusId = PendingGlobalId,
                Name = "Pendiente",
                Color = "#FB8C00",
                IsActive = true
            };
            EntityIdSetter.SetId(pendingStatus, PendingStatusId);
            CalendarStatusCompanyRepositoryMock.Setup(x => x.GetAsync(PendingStatusId)).ReturnsAsync(pendingStatus);
            var pendingGlobal = new CalendarStatus
            {
                Code = CalendarCodes.Statuses.Pending,
                Name = "Pendiente",
                Color = "#FB8C00",
                BlocksTime = true,
                IsActive = true
            };
            EntityIdSetter.SetId(pendingGlobal, PendingGlobalId);
            CalendarStatusRepositoryMock.Setup(x => x.GetAsync(PendingGlobalId)).ReturnsAsync(pendingGlobal);

            WebChannel = new CommunicationChannel
            {
                Code = CommunicationChannelCodes.Web,
                Name = "Web",
                IsActive = true
            };
            EntityIdSetter.SetId(WebChannel, WebChannelId);
            CommunicationChannelRepositoryMock.SetupRows(new[] { WebChannel });

            var actor = new ApplicationUser
            {
                Id = ActorUserId,
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = "ada@x.com"
            };
            ApplicationUserRepositoryMock.Setup(x => x.GetAsync(ActorUserId)).ReturnsAsync(actor);

            Master = new CalendarEvent
            {
                CompanyId = CompanyId,
                CalendarId = CalendarId,
                OwnerUserId = ActorUserId,
                CalendarActivityCompanyId = ActivityCompanyId,
                CalendarStatusCompanyId = PendingStatusId,
                Title = "Sync",
                StartUtc = EventStartUtc,
                EndUtc = EventEndUtc,
                IsAllDay = false,
                OriginChannelId = WebChannelId
            };
            EntityIdSetter.SetId(Master, EventId);
            CalendarEventsRepositoryMock.Setup(x => x.GetAsync(EventId)).ReturnsAsync(Master);
            CalendarEventsRepositoryMock
                .Setup(x => x.UpdateAsync(It.IsAny<CalendarEvent>()))
                .Callback<CalendarEvent>(e => UpdatedEvents.Add(e))
                .ReturnsAsync(true);
            CalendarEventsRepositoryMock
                .Setup(x => x.GetScheduleRowsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CalendarEventScheduleRow>());

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
        public Guid PendingStatusId { get; }
        public Guid RescheduledStatusId { get; }
        public Guid PendingGlobalId { get; }
        public Guid CancelledGlobalId { get; }
        public Guid ConfirmedGlobalId { get; }
        public Guid WebChannelId { get; }
        public Guid EventId { get; }
        public DateTime EventStartUtc { get; }
        public DateTime EventEndUtc { get; }

        public CalendarCompany Company { get; }
        public CommunicationChannel WebChannel { get; }
        public CalendarEvent Master { get; }

        public List<CalendarEvent> UpdatedEvents { get; } = new();
        public List<CalendarEventLog> CapturedLogs { get; } = new();

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
        public ChangeCalendarEventStatusCommandHandler CreateHandler()
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
            return new ChangeCalendarEventStatusCommandHandler(processor);
        }

        public ChangeCalendarEventStatusCommand BuildValidCommand()
        {
            return new ChangeCalendarEventStatusCommand
            {
                Id = EventId,
                CalendarStatusCompanyId = PendingStatusId,
                Notes = null
            };
        }
    }
}
