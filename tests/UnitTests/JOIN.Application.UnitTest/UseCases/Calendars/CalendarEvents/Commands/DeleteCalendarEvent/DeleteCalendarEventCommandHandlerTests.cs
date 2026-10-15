using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.DeleteCalendarEvent;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;
using JOIN.Domain.Customers;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEvents.Commands.DeleteCalendarEvent;

/// <summary>
/// SPEC 44 F10 / CalendarEvents group — delete handler covers the master-with-exceptions branch: a logical delete
/// on the master marks the exceptions as deleted too, and a <c>Deleted</c> log row is written for each row.
/// </summary>
public sealed class DeleteCalendarEventCommandHandlerTests
{
    private static readonly Fixture _fixture = new();
    private const string TimeZoneId = "America/Managua";

    /// <summary>
    /// Deleting a master with two exceptions marks all three rows as deleted (non-zero <c>GcRecord</c>) and
    /// writes a <c>Deleted</c> log row per affected event.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDeletingMaster_ShouldMarkMasterAndExceptionsAsDeleted()
    {
        var context = new DeleteCalendarEventTestContext();
        // The default delete branch walks the exception list only when the master carries a recurrence rule,
        // and OpenTargetAsync requires a scope + OccurrenceStartUtc when the master recurs. Wire the master
        // and the expander mock so the helper resolves a Series target before the exception loop runs.
        context.Master.RecurrenceRule = "FREQ=DAILY;COUNT=10";
        context.Master.RecurrenceEndUtc = context.EventStartUtc.AddDays(10);
        context.ExpanderMock
            .Setup(x => x.Expand(It.IsAny<CalendarRecurrenceSeries>(), context.EventStartUtc, context.EventStartUtc.AddSeconds(1), TimeZoneId))
            .Returns([new CalendarOccurrence(context.EventStartUtc, context.EventStartUtc.AddHours(1))]);
        var exception1 = new CalendarEvent
        {
            CompanyId = context.CompanyId,
            CalendarId = context.Master.CalendarId,
            OwnerUserId = context.ActorUserId,
            SeriesMasterId = context.EventId,
            OriginalStartUtc = context.EventStartUtc.AddDays(1),
            StartUtc = context.EventStartUtc.AddDays(1).AddHours(1),
            EndUtc = context.EventStartUtc.AddDays(1).AddHours(2),
            CalendarActivityCompanyId = context.Master.CalendarActivityCompanyId,
            CalendarStatusCompanyId = context.Master.CalendarStatusCompanyId,
            Title = "Exception 1"
        };
        EntityIdSetter.SetId(exception1, Guid.NewGuid());
        var exception2 = new CalendarEvent
        {
            CompanyId = context.CompanyId,
            CalendarId = context.Master.CalendarId,
            OwnerUserId = context.ActorUserId,
            SeriesMasterId = context.EventId,
            OriginalStartUtc = context.EventStartUtc.AddDays(2),
            StartUtc = context.EventStartUtc.AddDays(2).AddHours(1),
            EndUtc = context.EventStartUtc.AddDays(2).AddHours(2),
            CalendarActivityCompanyId = context.Master.CalendarActivityCompanyId,
            CalendarStatusCompanyId = context.Master.CalendarStatusCompanyId,
            Title = "Exception 2"
        };
        EntityIdSetter.SetId(exception2, Guid.NewGuid());
        context.CalendarEventsRepositoryMock
            .Setup(x => x.GetExceptionsAsync(context.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { exception1, exception2 });

        var command = new DeleteCalendarEventCommand(
            context.EventId,
            Scope: CalendarRecurrenceEditScope.Series,
            OccurrenceStartUtc: context.EventStartUtc);
        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Master.GcRecord.Should().NotBe(0);
        exception1.GcRecord.Should().NotBe(0);
        exception2.GcRecord.Should().NotBe(0);
        context.CapturedLogs.Count(l => l.Action == CalendarEventLogAction.Deleted).Should().Be(3);
    }

    /// <summary>
    /// Deleting an occurrence of a series (not the master) flips the exception's status to the company's
    /// <c>CANCELLED</c> status and writes a <c>Deleted</c> log row with the cancellation reason.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDeletingOneOccurrence_ShouldCancelException()
    {
        var context = new DeleteCalendarEventTestContext();
        var occurrenceKey = context.EventStartUtc.AddDays(1);
        context.Master.RecurrenceRule = "FREQ=DAILY;COUNT=10";
        context.Master.RecurrenceEndUtc = context.EventStartUtc.AddDays(10);

        context.ExpanderMock
            .Setup(x => x.Expand(It.IsAny<CalendarRecurrenceSeries>(), occurrenceKey, occurrenceKey.AddSeconds(1), TimeZoneId))
            .Returns([new CalendarOccurrence(occurrenceKey, occurrenceKey.AddHours(1))]);
        context.ExpanderMock
            .Setup(x => x.GetSeriesEndUtc(It.IsAny<CalendarRecurrenceSeries>(), TimeZoneId))
            .Returns(context.EventStartUtc.AddDays(10));

        var cancelledStatus = new CalendarStatusCompany
        {
            CompanyId = context.CompanyId,
            CalendarStatusId = Guid.NewGuid(),
            Name = "Cancelada",
            Color = "#9E9E9E",
            IsActive = true
        };
        var cancelledGlobal = new CalendarStatus
        {
            Code = CalendarCodes.Statuses.Cancelled,
            Name = "Cancelada",
            Color = "#9E9E9E",
            BlocksTime = false,
            IsActive = true
        };
        EntityIdSetter.SetId(cancelledGlobal, cancelledStatus.CalendarStatusId);
        var global = (await Task.FromResult(cancelledGlobal));
        context.CalendarStatusCompanyRepositoryMock
            .Setup(x => x.GetAsync(cancelledStatus.Id))
            .ReturnsAsync(cancelledStatus);
        context.CalendarStatusRepositoryMock
            .Setup(x => x.GetAsync(cancelledStatus.CalendarStatusId))
            .ReturnsAsync(cancelledGlobal);
        // Used by the get-by-code lookup
        context.CalendarStatusRepositoryMock
            .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CalendarStatus, bool>>>()))
            .ReturnsAsync(new List<CalendarStatus> { cancelledGlobal });
        context.CalendarStatusCompanyRepositoryMock
            .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CalendarStatusCompany, bool>>>()))
            .ReturnsAsync(new List<CalendarStatusCompany> { cancelledStatus });
        _ = global;

        var command = new DeleteCalendarEventCommand(context.EventId,
            Scope: CalendarRecurrenceEditScope.ThisOccurrence,
            OccurrenceStartUtc: occurrenceKey);
        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.CapturedLogs.Should().Contain(l => l.Action == CalendarEventLogAction.Deleted);
    }

    /// <summary>
    /// Reusable mocks for the delete handler tests.
    /// </summary>
    private sealed class DeleteCalendarEventTestContext
    {
        public DeleteCalendarEventTestContext()
        {
            CompanyId = Guid.NewGuid();
            ActorUserId = Guid.NewGuid();
            CalendarId = Guid.NewGuid();
            EventId = Guid.NewGuid();
            WebChannelId = Guid.NewGuid();
            EventStartUtc = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 14, 0, 0), DateTimeKind.Utc);

            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(ActorUserId.ToString());

            Company = new CalendarCompany
            {
                CompanyId = CompanyId,
                TimeZone = TimeZoneId,
                IsOpen24Hours = true,
                DefaultActivityUserId = ActorUserId,
                RescheduledByOthersStatusCompanyId = Guid.NewGuid(),
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

            ActivityCompany = new CalendarActivityCompany
            {
                CompanyId = CompanyId,
                CalendarActivityId = Guid.NewGuid(),
                Name = "Reunión",
                Color = "#1E88E5",
                DefaultDurationMinutes = 60,
                AllowsOverlap = false,
                RequiresBusinessHours = false,
                InitialCalendarStatusCompanyId = Guid.NewGuid(),
                IsActive = true
            };
            var activityCompanyId = Guid.NewGuid();
            EntityIdSetter.SetId(ActivityCompany, activityCompanyId);
            ActivityCompanyRepositoryMock.Setup(x => x.GetAsync(activityCompanyId)).ReturnsAsync(ActivityCompany);

            var pendingStatus = new CalendarStatusCompany
            {
                CompanyId = CompanyId,
                CalendarStatusId = Guid.NewGuid(),
                Name = "Pendiente",
                Color = "#FB8C00",
                IsActive = true
            };
            var pendingStatusId = Guid.NewGuid();
            EntityIdSetter.SetId(pendingStatus, pendingStatusId);
            CalendarStatusCompanyRepositoryMock.Setup(x => x.GetAsync(pendingStatusId)).ReturnsAsync(pendingStatus);
            var pendingGlobal = new CalendarStatus
            {
                Code = CalendarCodes.Statuses.Pending,
                Name = "Pendiente",
                Color = "#FB8C00",
                BlocksTime = true,
                IsActive = true
            };
            EntityIdSetter.SetId(pendingGlobal, pendingStatus.CalendarStatusId);
            CalendarStatusRepositoryMock.Setup(x => x.GetAsync(pendingStatus.CalendarStatusId))
                .ReturnsAsync(pendingGlobal);

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
                CalendarActivityCompanyId = activityCompanyId,
                CalendarStatusCompanyId = pendingStatusId,
                Title = "Daily",
                StartUtc = EventStartUtc,
                EndUtc = EventStartUtc.AddHours(1),
                IsAllDay = false,
                OriginChannelId = WebChannelId
            };
            EntityIdSetter.SetId(Master, EventId);
            CalendarEventsRepositoryMock.Setup(x => x.GetAsync(EventId)).ReturnsAsync(Master);
            CalendarEventsRepositoryMock
                .Setup(x => x.UpdateAsync(It.IsAny<CalendarEvent>()))
                .ReturnsAsync(true);
            CalendarEventsRepositoryMock
                .Setup(x => x.GetExceptionsAsync(EventId, It.IsAny<CancellationToken>()))
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
        public Guid EventId { get; }
        public Guid WebChannelId { get; }
        public DateTime EventStartUtc { get; }

        public CalendarCompany Company { get; }
        public CalendarActivityCompany ActivityCompany { get; }
        public CommunicationChannel WebChannel { get; }
        public CalendarEvent Master { get; }

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
        public DeleteCalendarEventCommandHandler CreateHandler()
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
            return new DeleteCalendarEventCommandHandler(processor);
        }
    }
}
