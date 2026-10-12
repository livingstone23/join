using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.UpdateCalendarEvent;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEvents.Commands.UpdateCalendarEvent;

/// <summary>
/// SPEC 44 F10 / CalendarEvents group — update handler covers the recurrence rules (R7) and the reschedule
/// status rule (R6): the time change by a non-owner sets the status to the company's
/// <c>RescheduledByOthersStatusCompanyId</c>, the owner keeps the original status, and non-time edits leave the
/// status untouched. Recurrence scope (ThisOccurrence / ThisAndFollowing / Series) drives the exception and split
/// branches.
/// </summary>
public sealed class UpdateCalendarEventCommandHandlerTests
{
    private static readonly Fixture _fixture = new();
    private const string TimeZoneId = "America/Managua";

    /// <summary>
    /// <c>ValidateRule</c> failure surfaces as <c>CALENDAR_INVALID_RECURRENCE_RULE</c>; nothing is persisted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRuleIsInvalid_ShouldReturnInvalidRecurrenceRule()
    {
        var context = new UpdateCalendarEventTestContext();
        context.ExpanderMock
            .Setup(x => x.Expand(It.IsAny<CalendarRecurrenceSeries>(), context.MasterStartUtc, context.MasterStartUtc.AddSeconds(1), TimeZoneId))
            .Returns([new CalendarOccurrence(context.MasterStartUtc, context.MasterStartUtc.AddHours(1))]);

        var command = context.BuildValidCommand() with
        {
            Scope = CalendarRecurrenceEditScope.Series,
            OccurrenceStartUtc = context.MasterStartUtc,
            RecurrenceRule = "BAD"
        };
        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.InvalidRecurrenceRule);
        context.CalendarEventsRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    /// <summary>
    /// A rule with <c>COUNT</c> greater than 730 (the documented maximum) is rejected by the parser; the expander
    /// never sees it, but the rule's own validation returns the same error code.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCountExceedsMaximum_ShouldReturnInvalidRecurrenceRule()
    {
        var context = new UpdateCalendarEventTestContext();
        context.ExpanderMock
            .Setup(x => x.Expand(It.IsAny<CalendarRecurrenceSeries>(), context.MasterStartUtc, context.MasterStartUtc.AddSeconds(1), TimeZoneId))
            .Returns([new CalendarOccurrence(context.MasterStartUtc, context.MasterStartUtc.AddHours(1))]);
        context.ExpanderMock
            .Setup(x => x.ValidateRule(It.IsAny<string>()))
            .Returns(Response<CalendarRecurrenceRule>.Error(CalendarErrorCodes.InvalidRecurrenceRule, ["COUNT must be <= 730."]));
        var rule = "FREQ=DAILY;COUNT=1000";
        var command = context.BuildValidCommand() with
        {
            Scope = CalendarRecurrenceEditScope.Series,
            OccurrenceStartUtc = context.MasterStartUtc,
            RecurrenceRule = rule
        };

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.InvalidRecurrenceRule);
    }

    /// <summary>
    /// <c>scope=ThisOccurrence</c> creates an exception: <c>SeriesMasterId</c> equals the master id,
    /// <c>OriginalStartUtc</c> equals the occurrence key, and a log row with action
    /// <c>OccurrenceModified</c> is written.
    /// </summary>
    [Fact]
    public async Task Handle_WhenScopeIsThisOccurrence_ShouldCreateExceptionAndLogOccurrenceModified()
    {
        var context = new UpdateCalendarEventTestContext();
        var occurrenceKey = context.MasterStartUtc;
        var newStart = occurrenceKey.AddHours(2);

        context.ExpanderMock
            .Setup(x => x.Expand(It.IsAny<CalendarRecurrenceSeries>(), occurrenceKey, occurrenceKey.AddSeconds(1), TimeZoneId))
            .Returns([new CalendarOccurrence(occurrenceKey, occurrenceKey.AddHours(1))]);
        context.ExpanderMock
            .Setup(x => x.GetSeriesEndUtc(It.IsAny<CalendarRecurrenceSeries>(), TimeZoneId))
            .Returns(occurrenceKey.AddDays(60));
        context.ExpanderMock
            .Setup(x => x.ValidateRule(It.IsAny<string>()))
            .Returns((string r) => CalendarRecurrenceRule.Parse(r));

        var command = context.BuildValidCommand() with
        {
            Scope = CalendarRecurrenceEditScope.ThisOccurrence,
            OccurrenceStartUtc = occurrenceKey,
            Start = new DateTimeOffset(newStart, TimeSpan.Zero),
            End = new DateTimeOffset(newStart.AddHours(1), TimeSpan.Zero),
            RecurrenceRule = null
        };

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message + " " + string.Join(" ", response.Errors ?? []));
        context.InsertedException.Should().NotBeNull();
        context.InsertedException!.SeriesMasterId.Should().Be(context.MasterId);
        context.InsertedException.OriginalStartUtc.Should().Be(occurrenceKey);
        context.InsertedException.StartUtc.Should().Be(newStart);
        context.CapturedLogs.Should().Contain(l => l.Action == CalendarEventLogAction.OccurrenceModified);
    }

    /// <summary>
    /// <c>scope=ThisAndFollowing</c> closes the original master with <c>UNTIL</c> = occurrence - 1s, creates a
    /// new master starting at the occurrence, and writes <c>SeriesSplit</c> log rows for both.
    /// </summary>
    [Fact]
    public async Task Handle_WhenScopeIsThisAndFollowing_ShouldSplitSeriesWithUntilOnOriginalMaster()
    {
        var context = new UpdateCalendarEventTestContext();
        var occurrenceKey = context.MasterStartUtc.AddDays(7);
        var series = CalendarRecurrenceRule.Parse("FREQ=DAILY;COUNT=10").Data!;
        context.Master.RecurrenceRule = series.ToRuleString();
        context.Master.RecurrenceEndUtc = context.MasterStartUtc.AddDays(10);

        context.ExpanderMock
            .Setup(x => x.Expand(It.IsAny<CalendarRecurrenceSeries>(), occurrenceKey, occurrenceKey.AddSeconds(1), TimeZoneId))
            .Returns([new CalendarOccurrence(occurrenceKey, occurrenceKey.AddHours(1))]);
        context.ExpanderMock
            .Setup(x => x.GetSeriesEndUtc(It.IsAny<CalendarRecurrenceSeries>(), TimeZoneId))
            .Returns((CalendarRecurrenceSeries s, string _) => CalendarRecurrenceRule.Parse(s.RecurrenceRule).Data?.UntilUtc);
        context.ExpanderMock
            .Setup(x => x.ValidateRule(It.IsAny<string>()))
            .Returns((string r) => CalendarRecurrenceRule.Parse(r));
        context.ExpanderMock
            .Setup(x => x.Expand(It.IsAny<CalendarRecurrenceSeries>(), context.MasterStartUtc, occurrenceKey, TimeZoneId))
            .Returns(Enumerable.Range(0, 7)
                .Select(i => new CalendarOccurrence(context.MasterStartUtc.AddDays(i), context.MasterStartUtc.AddDays(i).AddHours(1)))
                .ToList());

        var command = context.BuildValidCommand() with
        {
            Scope = CalendarRecurrenceEditScope.ThisAndFollowing,
            OccurrenceStartUtc = occurrenceKey,
            Start = new DateTimeOffset(occurrenceKey, TimeSpan.Zero),
            End = new DateTimeOffset(occurrenceKey.AddHours(1), TimeSpan.Zero),
            RecurrenceRule = "FREQ=DAILY;COUNT=5"
        };

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message + " " + string.Join(" ", response.Errors ?? []));
        // SplitSeriesAsync adds UNTIL to the original master, then inserts the new master
        // and updates it again from ApplyUpdateAsync. The first update is the master with UNTIL.
        var originalUpdated = context.UpdatedMasters.First();
        originalUpdated.RecurrenceRule!.Should().Contain("UNTIL=");
        context.InsertedNewMaster.Should().NotBeNull();
        context.InsertedNewMaster!.StartUtc.Should().Be(occurrenceKey);
        context.InsertedNewMaster.RecurrenceRule.Should().Be("FREQ=DAILY;COUNT=5");
        context.CapturedLogs.Count(l => l.Action == CalendarEventLogAction.SeriesSplit).Should().Be(2);
    }

    /// <summary>
    /// <c>scope=Series</c> modifies the master only; existing exceptions are preserved and revalidated.
    /// </summary>
    [Fact]
    public async Task Handle_WhenScopeIsSeries_ShouldUpdateMasterOnlyAndPreserveExceptions()
    {
        var context = new UpdateCalendarEventTestContext();
        var exception = new CalendarEvent
        {
            CompanyId = context.CompanyId,
            CalendarId = context.Master.CalendarId,
            OwnerUserId = context.ActorUserId,
            SeriesMasterId = context.MasterId,
            OriginalStartUtc = context.MasterStartUtc.AddDays(2),
            StartUtc = context.MasterStartUtc.AddDays(2).AddHours(2),
            EndUtc = context.MasterStartUtc.AddDays(2).AddHours(3),
            Title = "Modified occurrence",
            CalendarActivityCompanyId = context.Master.CalendarActivityCompanyId,
            CalendarStatusCompanyId = context.Master.CalendarStatusCompanyId
        };
        EntityIdSetter.SetId(exception, Guid.NewGuid());
        context.CalendarEventsRepositoryMock
            .Setup(x => x.GetExceptionsAsync(context.MasterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { exception });
        context.ExpanderMock
            .Setup(x => x.Expand(It.IsAny<CalendarRecurrenceSeries>(), context.MasterStartUtc, context.MasterStartUtc.AddSeconds(1), TimeZoneId))
            .Returns([new CalendarOccurrence(context.MasterStartUtc, context.MasterStartUtc.AddHours(1))]);
        context.ExpanderMock
            .Setup(x => x.GetSeriesEndUtc(It.IsAny<CalendarRecurrenceSeries>(), TimeZoneId))
            .Returns((CalendarRecurrenceSeries _, string _) => (DateTime?)null);

        var newStart = context.MasterStartUtc.AddHours(1);
        var command = context.BuildValidCommand() with
        {
            Scope = CalendarRecurrenceEditScope.Series,
            OccurrenceStartUtc = context.MasterStartUtc,
            Start = new DateTimeOffset(newStart, TimeSpan.Zero),
            End = new DateTimeOffset(newStart.AddHours(1), TimeSpan.Zero)
        };

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message + " " + string.Join(" ", response.Errors ?? []));
        context.UpdatedMasters.Should().Contain(m => m.Id == context.MasterId);
        context.InsertedException.Should().BeNull();
    }

    /// <summary>
    /// R2: <c>CalendarEvents</c> works on the current user's activities only. Another user's activity answers
    /// <c>CALENDAR_EVENT_NOT_FOUND</c> (never 403) whatever changes; rescheduling by a non-owner goes through
    /// <c>CalendarPanel</c> (see <c>UpdateCalendarPanelEventCommandHandlerTests</c>).
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherUserReschedulesFromTheOwnCalendar_ShouldReturnEventNotFound()
    {
        var context = new UpdateCalendarEventTestContext();
        context.Master.OwnerUserId = Guid.NewGuid();
        context.Master.RecurrenceRule = null;
        var handler = context.CreateHandler();

        var response = await handler.Handle(new UpdateCalendarEventCommand
        {
            Id = context.MasterId,
            CalendarActivityCompanyId = context.Master.CalendarActivityCompanyId,
            Title = "Renamed",
            Start = new DateTimeOffset(context.MasterStartUtc.AddHours(2), TimeSpan.Zero),
            End = new DateTimeOffset(context.MasterStartUtc.AddHours(2 + 1), TimeSpan.Zero)
        }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventNotFound);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// When the owner reschedules their own activity, the status is NOT changed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRescheduledByOwner_ShouldKeepOriginalStatus()
    {
        var context = new UpdateCalendarEventTestContext();
        context.ExpanderMock
            .Setup(x => x.Expand(It.IsAny<CalendarRecurrenceSeries>(), context.MasterStartUtc, context.MasterStartUtc.AddSeconds(1), TimeZoneId))
            .Returns([new CalendarOccurrence(context.MasterStartUtc, context.MasterStartUtc.AddHours(1))]);
        context.ExpanderMock
            .Setup(x => x.GetSeriesEndUtc(It.IsAny<CalendarRecurrenceSeries>(), TimeZoneId))
            .Returns((DateTime?)null);
        var originalStatusId = context.Master.CalendarStatusCompanyId;

        var newStart = context.MasterStartUtc.AddHours(2);
        var command = context.BuildValidCommand() with
        {
            Scope = CalendarRecurrenceEditScope.Series,
            OccurrenceStartUtc = context.MasterStartUtc,
            Start = new DateTimeOffset(newStart, TimeSpan.Zero),
            End = new DateTimeOffset(newStart.AddHours(1), TimeSpan.Zero)
        };

        var response = await context.CreateHandler().Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message + " " + string.Join(" ", response.Errors ?? []));
        var updated = context.UpdatedMasters.Last();
        updated.CalendarStatusCompanyId.Should().Be(originalStatusId);
    }

    /// <summary>
    /// R2: <c>CalendarEvents</c> works on the current user's activities only. Another user's activity answers
    /// <c>CALENDAR_EVENT_NOT_FOUND</c> (never 403) whatever changes; rescheduling by a non-owner goes through
    /// <c>CalendarPanel</c> (see <c>UpdateCalendarPanelEventCommandHandlerTests</c>).
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherUserRenamesFromTheOwnCalendar_ShouldReturnEventNotFound()
    {
        var context = new UpdateCalendarEventTestContext();
        context.Master.OwnerUserId = Guid.NewGuid();
        context.Master.RecurrenceRule = null;
        var handler = context.CreateHandler();

        var response = await handler.Handle(new UpdateCalendarEventCommand
        {
            Id = context.MasterId,
            CalendarActivityCompanyId = context.Master.CalendarActivityCompanyId,
            Title = "Renamed",
            Start = new DateTimeOffset(context.MasterStartUtc.AddHours(0), TimeSpan.Zero),
            End = new DateTimeOffset(context.MasterStartUtc.AddHours(0 + 1), TimeSpan.Zero)
        }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventNotFound);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Reusable mocks for the update handler tests. Seeds a recurring master owned by the current user.
    /// </summary>
    private sealed class UpdateCalendarEventTestContext
    {
        public UpdateCalendarEventTestContext()
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
                DefaultCalendarName = "Mi calendario"
            };
            CalendarCompanyRepositoryMock.SetupRows(new[] { Company });

            UserCalendar = new UserCalendar
            {
                CompanyId = CompanyId,
                OwnerUserId = ActorUserId,
                Name = "Mi calendario",
                Color = "#1E88E5",
                IsActive = true
            };
            UserCalendarRepositoryMock.Setup(x => x.GetAsync(CalendarId)).ReturnsAsync(UserCalendar);

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
            // BuildResultAsync looks up the channel by id; SetupRows only configures the predicate-based read.
            CommunicationChannelRepositoryMock.Setup(x => x.GetAsync(WebChannelId)).ReturnsAsync(WebChannel);

            ApplicationUser actor = new()
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
                CalendarStatusCompanyId = InitialStatusId,
                Title = "Daily",
                StartUtc = MasterStartUtc,
                EndUtc = MasterEndUtc,
                IsAllDay = false,
                RecurrenceRule = "FREQ=DAILY;COUNT=10",
                RecurrenceEndUtc = MasterStartUtc.AddDays(10),
                OriginChannelId = WebChannelId
            };
            EntityIdSetter.SetId(Master, MasterId);
            CalendarEventsRepositoryMock.Setup(x => x.GetAsync(MasterId)).ReturnsAsync(Master);
            CalendarEventsRepositoryMock
                .Setup(x => x.UpdateAsync(It.IsAny<CalendarEvent>()))
                .Callback<CalendarEvent>(e => UpdatedMasters.Add(e))
                .ReturnsAsync(true);
            CalendarEventsRepositoryMock
                .Setup(x => x.InsertAsync(It.IsAny<CalendarEvent>()))
                .Callback<CalendarEvent>(e =>
                {
                    if (e.SeriesMasterId == MasterId)
                    {
                        InsertedException = e;
                    }
                    else
                    {
                        InsertedNewMaster = e;
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
            UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(PersonRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Customer>()).Returns(CustomerRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Ticket>()).Returns(TicketRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Project>()).Returns(ProjectRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<UserCompany>()).Returns(UserCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CalendarUserConfiguration>()).Returns(CalendarUserConfigurationRepositoryMock.Object);
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
        public Guid MasterId { get; }
        public DateTime MasterStartUtc { get; }
        public DateTime MasterEndUtc { get; }

        public CalendarCompany Company { get; }
        public UserCalendar UserCalendar { get; }
        public CalendarActivityCompany ActivityCompany { get; }
        public CalendarStatusCompany InitialStatus { get; }
        public CommunicationChannel WebChannel { get; }
        public CalendarEvent Master { get; }

        public CalendarEvent? InsertedException { get; private set; }
        public CalendarEvent? InsertedNewMaster { get; private set; }
        public List<CalendarEvent> UpdatedMasters { get; } = new();
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
        public UpdateCalendarEventCommandHandler CreateHandler()
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
            return new UpdateCalendarEventCommandHandler(processor);
        }

        public UpdateCalendarEventCommand BuildValidCommand()
        {
            return new UpdateCalendarEventCommand
            {
                Id = MasterId,
                Scope = null,
                OccurrenceStartUtc = null,
                CalendarId = CalendarId,
                CalendarActivityCompanyId = ActivityCompanyId,
                Title = "Daily (edited)",
                Start = new DateTimeOffset(MasterStartUtc, TimeSpan.Zero),
                End = new DateTimeOffset(MasterEndUtc, TimeSpan.Zero),
                IsAllDay = false
            };
        }
    }
}
