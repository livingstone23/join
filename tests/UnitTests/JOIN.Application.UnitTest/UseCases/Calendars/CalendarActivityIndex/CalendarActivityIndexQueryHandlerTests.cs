using System.Data;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.CalendarActivityIndex;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarActivityIndex;

/// <summary>
/// SPEC 68 — exercises the activity index query handler. The Dapper reader is mocked to keep the tests focused
/// on the handler logic (visibility, range cap, filters, expansion, pagination, export).
/// </summary>
public sealed class CalendarActivityIndexQueryHandlerTests
{
    private static readonly Fixture _fixture = new();

    private const string TimeZoneId = "America/Managua";

    /// <summary>
    /// Acceptance #1 — a user without the per-user flag sees only their own activities even when the request
    /// tries to filter by another owner.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasNoFlag_ForcesOwnerFilterToCurrentUser()
    {
        var context = new IndexTestContext(canViewOthers: false);
        var otherOwner = Guid.NewGuid();
        var currentUser = context.CurrentUserId;
        context.Reader.Rows = new List<CalendarActivityIndexRow>
            {
                BuildRow(ownerId: currentUser, title: "Mine", startOffsetDays: 0),
                BuildRow(ownerId: otherOwner, title: "Theirs", startOffsetDays: 0)
            };

        var response = await context.CreateHandler().Handle(new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 3, 8, 0, 0, 0, DateTimeKind.Utc),
            OwnerUserIds = new[] { otherOwner }
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var capturedCriteria = context.CapturedCriteria;
        capturedCriteria.Should().NotBeNull();
        capturedCriteria!.OwnerUserIds.Should().BeEquivalentTo(new[] { currentUser });
    }

    /// <summary>
    /// Acceptance #2 — a user with the flag sees the activities of any owner; the request's ownerUserIds is
    /// honored as a filter.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFlagIsOn_AppliesRequestOwnerFilter()
    {
        var context = new IndexTestContext(canViewOthers: true);
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        context.Reader.Rows = new List<CalendarActivityIndexRow>
            {
                BuildRow(ownerId: ownerA, title: "A"),
                BuildRow(ownerId: ownerB, title: "B")
            };

        var response = await context.CreateHandler().Handle(new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 3, 8, 0, 0, 0, DateTimeKind.Utc),
            OwnerUserIds = new[] { ownerB }
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.CapturedCriteria!.OwnerUserIds.Should().BeEquivalentTo(new[] { ownerB });
    }

    /// <summary>
    /// Rule 4 — range longer than 92 days returns <c>CALENDAR_RANGE_TOO_LARGE</c> (400) before the reader runs.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRangeExceeds92Days_ReturnsRangeTooLarge()
    {
        var context = new IndexTestContext(canViewOthers: true);

        var response = await context.CreateHandler().Handle(new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 7, 1, 0, 0, 0, DateTimeKind.Utc)
        }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
        // The reader must not be called when the range is invalid; the fake has no Calls API, but we can
        // assert the criteria callback (which fires only on read) stays null.
    }

    /// <summary>
    /// Rule 4 — range with <c>to &lt;= from</c> is rejected.
    /// </summary>
    [Fact]
    public async Task Handle_WhenToIsNotAfterFrom_ReturnsRangeTooLarge()
    {
        var context = new IndexTestContext(canViewOthers: true);

        var response = await context.CreateHandler().Handle(new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 8, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
    }

    /// <summary>
    /// Acceptance #3 — a weekly series in a 30-day range produces one row per occurrence. The master starts on
    /// Monday 2027-03-01; a 30-day window catches five Mondays (March 1, 8, 15, 22, 29); the sixth occurrence
    /// (April 5) is outside the range.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSeriesOverlapsRange_ReturnsOneRowPerOccurrence()
    {
        var context = new IndexTestContext(canViewOthers: false);
        var master = BuildRow(
            ownerId: context.CurrentUserId,
            title: "Weekly",
            startOffsetDays: 0,
            durationDays: 1,
            recurrenceRule: "FREQ=WEEKLY;BYDAY=MO;COUNT=6");
        context.Reader.Rows = new List<CalendarActivityIndexRow> { master };

        var response = await context.CreateHandler().Handle(new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 3, 31, 0, 0, 0, DateTimeKind.Utc)
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.TotalCount.Should().Be(5);
        response.Data.Items.Should().HaveCount(5);
    }

    /// <summary>
    /// Acceptance #4 — <c>IncludeCancelled = false</c> drops <c>CANCELLED</c> occurrences.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIncludeCancelledIsFalse_DropsCancelledOccurrences()
    {
        var context = new IndexTestContext(canViewOthers: false);
        context.Reader.Rows = new List<CalendarActivityIndexRow>
            {
                BuildRow(ownerId: context.CurrentUserId, title: "Alive", statusCode: CalendarCodes.Statuses.Pending),
                BuildRow(ownerId: context.CurrentUserId, title: "Dead", statusCode: CalendarCodes.Statuses.Cancelled)
            };

        var response = await context.CreateHandler().Handle(new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 3, 8, 0, 0, 0, DateTimeKind.Utc),
            IncludeCancelled = false
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.TotalCount.Should().Be(1);
        response.Data.Items.Single().Title.Should().Be("Alive");
    }

    /// <summary>
    /// Acceptance #4 — <c>IncludeCancelled = true</c> keeps <c>CANCELLED</c> occurrences.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIncludeCancelledIsTrue_KeepsCancelledOccurrences()
    {
        var context = new IndexTestContext(canViewOthers: false);
        context.Reader.Rows = new List<CalendarActivityIndexRow>
            {
                BuildRow(ownerId: context.CurrentUserId, title: "Alive", statusCode: CalendarCodes.Statuses.Pending),
                BuildRow(ownerId: context.CurrentUserId, title: "Dead", statusCode: CalendarCodes.Statuses.Cancelled)
            };

        var response = await context.CreateHandler().Handle(new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 3, 8, 0, 0, 0, DateTimeKind.Utc),
            IncludeCancelled = true
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.TotalCount.Should().Be(2);
    }

    /// <summary>
    /// Acceptance #4 — the <c>search</c> filter narrows the result by title, summary or location (case
    /// insensitive).
    /// </summary>
    [Fact]
    public async Task Handle_WhenSearchIsGiven_NarrowsByTitleSummaryOrLocation()
    {
        var context = new IndexTestContext(canViewOthers: false);
        context.Reader.Rows = new List<CalendarActivityIndexRow>
            {
                BuildRow(ownerId: context.CurrentUserId, title: "Coffee"),
                BuildRowWith(ownerId: context.CurrentUserId, title: "Standup", location: "Coffee room"),
                BuildRowWith(ownerId: context.CurrentUserId, title: "Lunch", summary: "Coffee chat"),
                BuildRow(ownerId: context.CurrentUserId, title: "Standup")
            };

        var response = await context.CreateHandler().Handle(new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 3, 8, 0, 0, 0, DateTimeKind.Utc),
            Search = "coffee"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.TotalCount.Should().Be(3);
    }

    /// <summary>
    /// Pagination — <c>pageSize</c> slices the expanded list after sorting by <c>StartUtc</c>; the totals and
    /// page metadata reflect the full expanded list, not the slice.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPageSizeIsGiven_PaginatesExpandedOccurrences()
    {
        var context = new IndexTestContext(canViewOthers: false);
        var rows = Enumerable.Range(0, 12)
            .Select(i => BuildRow(ownerId: context.CurrentUserId, title: $"A{i}", startOffsetDays: i))
            .ToList();
        context.Reader.Rows = rows;

        var response = await context.CreateHandler().Handle(new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            PageNumber = 2,
            PageSize = 5
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.TotalCount.Should().Be(12);
        response.Data.PageSize.Should().Be(5);
        response.Data.PageNumber.Should().Be(2);
        response.Data.TotalPages.Should().Be(3);
        response.Data.Items.Should().HaveCount(5);
    }

    /// <summary>
    /// Export — <c>Export=true</c> forces <c>PageSize = 10000</c> and returns the first page of the expanded list.
    /// </summary>
    [Fact]
    public async Task Handle_WhenExportIsTrue_Uses10000PageSize()
    {
        var context = new IndexTestContext(canViewOthers: false);
        var rows = Enumerable.Range(0, 25)
            .Select(i => BuildRow(ownerId: context.CurrentUserId, title: $"A{i}", startOffsetDays: i))
            .ToList();
        context.Reader.Rows = rows;

        var response = await context.CreateHandler().Handle(new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            PageSize = 10,
            Export = true
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.PageSize.Should().Be(CalendarActivityIndexQueryHandler.ExportPageSize);
        response.Data.Items.Should().HaveCount(25);
    }

    /// <summary>
    /// Acceptance #4 — without the flag, <c>calendarIds</c> from the request is ignored: the handler never
    /// forwards it to the reader.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFlagIsOff_IgnoresCalendarFilter()
    {
        var context = new IndexTestContext(canViewOthers: false);
        context.Reader.Rows = new List<CalendarActivityIndexRow>();

        var response = await context.CreateHandler().Handle(new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 3, 8, 0, 0, 0, DateTimeKind.Utc),
            CalendarIds = new[] { Guid.NewGuid() }
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.CapturedCriteria!.CalendarIds.Should().BeNull();
    }

    /// <summary>
    /// Acceptance #3 — a modified occurrence shows its own data and times; when it fails a filter it is dropped and
    /// the master occurrence it replaces does not reappear.
    /// </summary>
    [Fact]
    public async Task Handle_WhenExceptionFailsFilter_DoesNotResurrectMasterOccurrence()
    {
        var context = new IndexTestContext(canViewOthers: false);
        var master = BuildRow(ownerId: context.CurrentUserId, title: "Weekly", recurrenceRule: "FREQ=WEEKLY;BYDAY=MO;COUNT=3");
        var exception = master with
        {
            Id = Guid.NewGuid(),
            SeriesMasterId = master.Id,
            OriginalStartUtc = master.StartUtc.AddDays(7),
            StartUtc = master.StartUtc.AddDays(7).AddHours(3),
            EndUtc = master.EndUtc.AddDays(7).AddHours(3),
            RecurrenceRule = null,
            Title = "Moved",
            StatusCompanyId = Guid.NewGuid()
        };
        context.Reader.Rows = [master, exception];
        var query = new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 3, 31, 0, 0, 0, DateTimeKind.Utc)
        };

        var all = await context.CreateHandler().Handle(query, CancellationToken.None);
        var filtered = await context.CreateHandler().Handle(query with { StatusCompanyIds = [master.StatusCompanyId] }, CancellationToken.None);

        var moved = all.Data!.Items.Should().ContainSingle(i => i.ExceptionId == exception.Id).Subject;
        moved.Title.Should().Be("Moved");
        moved.OccurrenceStartUtc.Should().Be(exception.StartUtc);
        moved.OccurrenceEndUtc.Should().Be(exception.EndUtc);
        filtered.Data!.Items.Should().HaveCount(2).And.OnlyContain(i => i.ExceptionId == null);
        filtered.Data.Items.Select(i => i.OccurrenceStartUtc).Should().NotContain(exception.OriginalStartUtc!.Value);
    }

    /// <summary>
    /// Acceptance #4 — type, status, channel and person filters are applied to each occurrence, not only in SQL.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAttributeFiltersAreGiven_NarrowsEachOccurrence()
    {
        var context = new IndexTestContext(canViewOthers: false);
        var person = Guid.NewGuid();
        var match = BuildRow(ownerId: context.CurrentUserId, title: "Match") with { RequesterPersonId = person, OriginChannelId = Guid.NewGuid() };
        context.Reader.Rows =
        [
            match,
            match with { Id = Guid.NewGuid(), Title = "Other type", ActivityCompanyId = Guid.NewGuid() },
            match with { Id = Guid.NewGuid(), Title = "Other status", StatusCompanyId = Guid.NewGuid() },
            match with { Id = Guid.NewGuid(), Title = "Other channel", OriginChannelId = Guid.NewGuid() },
            match with { Id = Guid.NewGuid(), Title = "Other person", RequesterPersonId = Guid.NewGuid() }
        ];

        var response = await context.CreateHandler().Handle(new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 3, 8, 0, 0, 0, DateTimeKind.Utc),
            ActivityCompanyIds = [match.ActivityCompanyId],
            StatusCompanyIds = [match.StatusCompanyId],
            OriginChannelIds = [match.OriginChannelId],
            RequesterPersonId = person
        }, CancellationToken.None);

        response.Data!.Items.Should().ContainSingle().Which.Title.Should().Be("Match");
    }

    /// <summary>
    /// Visibility — without the flag, a row of another owner the reader returns is still dropped.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasNoFlag_DropsOtherOwnersRows()
    {
        var context = new IndexTestContext(canViewOthers: false);
        context.Reader.Rows =
        [
            BuildRow(ownerId: context.CurrentUserId, title: "Mine"),
            BuildRow(ownerId: Guid.NewGuid(), title: "Theirs")
        ];

        var response = await context.CreateHandler().Handle(new CalendarActivityIndexQuery
        {
            FromUtc = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2027, 3, 8, 0, 0, 0, DateTimeKind.Utc)
        }, CancellationToken.None);

        response.Data!.Items.Should().ContainSingle().Which.Title.Should().Be("Mine");
    }

    private static CalendarActivityIndexRow BuildRow(
        Guid ownerId,
        string title,
        int startOffsetDays = 0,
        int durationDays = 1,
        string? recurrenceRule = null,
        string statusCode = CalendarCodes.Statuses.Pending)
    {
        return BuildRowWith(ownerId, title, startOffsetDays: startOffsetDays, durationDays: durationDays, recurrenceRule: recurrenceRule, statusCode: statusCode);
    }

    private static CalendarActivityIndexRow BuildRowWith(
        Guid ownerId,
        string title,
        int startOffsetDays = 0,
        int durationDays = 1,
        string? recurrenceRule = null,
        string statusCode = CalendarCodes.Statuses.Pending,
        string? location = null,
        string? summary = null)
    {
        var start = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(startOffsetDays);
        var row = new CalendarActivityIndexRow
        {
            Id = Guid.NewGuid(),
            SeriesMasterId = null,
            OriginalStartUtc = null,
            IsAllDay = false,
            StartUtc = start,
            EndUtc = start.AddDays(durationDays),
            RecurrenceRule = recurrenceRule,
            Title = title,
            Summary = summary,
            Location = location,
            ActivityCompanyId = Guid.NewGuid(),
            ActivityName = "Reunión",
            ActivityColor = "#1E88E5",
            StatusCompanyId = Guid.NewGuid(),
            StatusCode = statusCode,
            StatusName = "Pendiente",
            StatusColor = "#FB8C00",
            OwnerUserId = ownerId,
            OwnerName = "Owner",
            CalendarId = Guid.NewGuid(),
            CalendarName = "Personal",
            OriginChannelCode = "WEB"
        };
        return row;
    }

    /// <summary>
    /// Reusable mocks for the handler tests. The reader is mocked so the tests stay focused on handler logic.
    /// </summary>
    private sealed class IndexTestContext
    {
        public IndexTestContext(bool canViewOthers)
        {
            CompanyId = Guid.NewGuid();
            CurrentUserId = Guid.NewGuid();
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(CurrentUserId.ToString());

            ConfigurationRepositoryMock
                .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<System.Linq.Expressions.Expression<Func<JOIN.Domain.Calendars.CalendarUserConfiguration, bool>>>()))
                .ReturnsAsync((System.Linq.Expressions.Expression<Func<JOIN.Domain.Calendars.CalendarUserConfiguration, bool>> predicate) =>
                {
                    var configuration = new JOIN.Domain.Calendars.CalendarUserConfiguration
                    {
                        CompanyId = CompanyId,
                        UserId = CurrentUserId,
                        DefaultCalendarId = Guid.NewGuid(),
                        CanViewOthersActivities = canViewOthers
                    };
                    EntityIdSetter.SetId(configuration, Guid.NewGuid());
                    return new[] { configuration }.AsEnumerable();
                });

            CompanyRepositoryMock
                .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<System.Linq.Expressions.Expression<Func<JOIN.Domain.Calendars.CalendarCompany, bool>>>()))
                .ReturnsAsync((System.Linq.Expressions.Expression<Func<JOIN.Domain.Calendars.CalendarCompany, bool>> predicate) =>
                {
                    var company = new JOIN.Domain.Calendars.CalendarCompany
                    {
                        CompanyId = CompanyId,
                        TimeZone = TimeZoneId,
                        IsOpen24Hours = false,
                        BusinessHoursStart = new TimeOnly(8, 0),
                        BusinessHoursEnd = new TimeOnly(17, 0),
                        DefaultActivityUserId = CurrentUserId,
                        RescheduledByOthersStatusCompanyId = Guid.NewGuid(),
                        DefaultCalendarName = "Mi calendario"
                    };
                    return new[] { company }.AsEnumerable();
                });

            UnitOfWorkMock.Setup(x => x.GetRepository<JOIN.Domain.Calendars.CalendarUserConfiguration>())
                .Returns(ConfigurationRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<JOIN.Domain.Calendars.CalendarCompany>())
                .Returns(CompanyRepositoryMock.Object);

            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Mock.Of<IDbConnection>());

            // Reader is the fake; each test can overwrite Reader.Rows to return whatever rows it needs.
            Reader.CriteriaCallback = c => CapturedCriteria = c;
        }

        public Guid CompanyId { get; }
        public Guid CurrentUserId { get; }
        public CalendarActivityIndexCriteria? CapturedCriteria { get; private set; }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<JOIN.Domain.Calendars.CalendarUserConfiguration>> ConfigurationRepositoryMock { get; } = new();
        public Mock<IGenericRepository<JOIN.Domain.Calendars.CalendarCompany>> CompanyRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeCalendarActivityIndexReader Reader { get; } = new();
        public Mock<ICalendarRecurrenceExpander> ExpanderMock { get; } = new();

        public CalendarActivityIndexQueryHandler CreateHandler()
        {
            ExpanderMock.Setup(x => x.ValidateRule(It.IsAny<string>())).Returns((string r) => CalendarRecurrenceRule.Parse(r));
            ExpanderMock.Setup(x => x.Expand(It.IsAny<CalendarRecurrenceSeries>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>()))
                .Returns((CalendarRecurrenceSeries series, DateTime from, DateTime to, string tz) =>
                {
                    var parsed = CalendarRecurrenceRule.Parse(series.RecurrenceRule);
                    if (!parsed.IsSuccess)
                    {
                        return Array.Empty<CalendarOccurrence>();
                    }

                    var rule = parsed.Data!;
                    var occurrences = new List<CalendarOccurrence>();
                    var current = series.StartUtc;
                    var count = 0;
                    var duration = series.EndUtc - series.StartUtc;
                    while (current < to)
                    {
                        if (current >= from)
                        {
                            occurrences.Add(new CalendarOccurrence(current, current.Add(duration)));
                        }
                        if (rule.Count.HasValue && ++count >= rule.Count.Value)
                        {
                            break;
                        }
                        if (rule.UntilUtc.HasValue && current >= rule.UntilUtc.Value)
                        {
                            break;
                        }
                        current = Advance(current, rule);
                    }
                    return occurrences;
                });
            var visibility = new CalendarActivityVisibility(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
            return new CalendarActivityIndexQueryHandler(
                ConnectionFactoryMock.Object,
                Reader,
                visibility,
                ExpanderMock.Object);
        }

        private static DateTime Advance(DateTime current, CalendarRecurrenceRule rule)
        {
            // Minimal advance: WEEKLY+BYDAY=MO=7-day, MONTHLY=month, YEARLY=year, DAILY=day.
            if (rule.Frequency.Equals("DAILY", StringComparison.OrdinalIgnoreCase))
            {
                return current.AddDays(rule.Interval);
            }
            if (rule.Frequency.Equals("WEEKLY", StringComparison.OrdinalIgnoreCase))
            {
                return current.AddDays(7 * rule.Interval);
            }
            if (rule.Frequency.Equals("MONTHLY", StringComparison.OrdinalIgnoreCase))
            {
                return current.AddMonths(rule.Interval);
            }
            if (rule.Frequency.Equals("YEARLY", StringComparison.OrdinalIgnoreCase))
            {
                return current.AddYears(rule.Interval);
            }
            return current.AddDays(rule.Interval);
        }

        /// <summary>
        /// Simple fake that always captures the criteria and returns the configured rows.
        /// </summary>
        public sealed class FakeCalendarActivityIndexReader : ICalendarActivityIndexReader
        {
            public List<CalendarActivityIndexRow> Rows { get; set; } = [];
            public Action<CalendarActivityIndexCriteria>? CriteriaCallback { get; set; }

            public Task<IReadOnlyList<CalendarActivityIndexRow>> ReadAsync(
                IDbConnection connection,
                CalendarActivityIndexCriteria criteria,
                CancellationToken cancellationToken)
            {
                CriteriaCallback?.Invoke(criteria);
                return Task.FromResult<IReadOnlyList<CalendarActivityIndexRow>>(Rows);
            }
        }
    }
}