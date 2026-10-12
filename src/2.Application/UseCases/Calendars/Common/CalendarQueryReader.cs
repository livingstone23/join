using System.Data;
using Dapper;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Dapper reads shared by the calendar range queries (SPEC 44, "Implementación de las consultas"): one-time
/// activities that intersect the range, series masters that reach it and their exceptions, then the expansion in
/// memory with <see cref="CalendarOccurrenceExpansion"/>. Every query filters
/// <c>CompanyId = @TenantId AND GcRecord = 0</c>; ranges are <c>timestamptz</c> parameters in UTC, owners and masters
/// are arrays (<c>= ANY(@Owners)</c>).
/// </summary>
public sealed class CalendarQueryReader(ISqlConnectionFactory connectionFactory, ICalendarRecurrenceExpander expander)
{
    /// <summary> Maximum range of a view query: a month view with its edge weeks. </summary>
    public const int MaxRangeDays = 62;

    /// <summary> Maximum number of users in a panel query. </summary>
    public const int MaxPanelUsers = 20;

    private const string EventColumns = """
        e.Id,
        e.SeriesMasterId,
        e.OriginalStartUtc,
        e.CalendarId,
        uc.Name AS CalendarName,
        uc.Color AS CalendarColor,
        e.OwnerUserId,
        CONCAT(u.FirstName, ' ', u.LastName) AS OwnerName,
        e.Title,
        e.Summary,
        e.Location,
        e.VideoCallUrl,
        e.IsAllDay,
        e.StartUtc,
        e.EndUtc,
        e.RecurrenceRule,
        e.RecurrenceEndUtc,
        e.CalendarActivityCompanyId AS ActivityCompanyId,
        a.Code AS ActivityCode,
        ac.Name AS ActivityName,
        ac.Color AS ActivityColor,
        ac.AllowsOverlap,
        e.CalendarStatusCompanyId AS StatusCompanyId,
        s.Code AS StatusCode,
        sc.Name AS StatusName,
        sc.Color AS StatusColor,
        s.BlocksTime,
        s.IsFinal,
        ch.Code AS ChannelCode,
        e.OriginType,
        e.OriginIdentifier,
        e.OriginUserId
        """;

    private const string EventJoins = """
        FROM Calendar.Events e
        INNER JOIN Calendar.UserCalendars uc ON uc.Id = e.CalendarId
        INNER JOIN Security.Users u ON u.Id = e.OwnerUserId
        INNER JOIN Calendar.ActivityCompanies ac ON ac.Id = e.CalendarActivityCompanyId
        INNER JOIN Calendar.Activities a ON a.Id = ac.CalendarActivityId
        INNER JOIN Calendar.StatusCompanies sc ON sc.Id = e.CalendarStatusCompanyId
        INNER JOIN Calendar.Statuses s ON s.Id = sc.CalendarStatusId
        INNER JOIN Common.CommunicationChannels ch ON ch.Id = e.OriginChannelId
        """;

    private static readonly string RangeSql = $"""
        SELECT
        {EventColumns}
        {EventJoins}
        WHERE e.CompanyId = @TenantId
          AND e.GcRecord = 0
          AND e.OwnerUserId = ANY(@Owners)
          AND e.SeriesMasterId IS NULL
          AND e.StartUtc < @To
          AND ((e.RecurrenceRule IS NULL AND e.EndUtc > @From)
               OR (e.RecurrenceRule IS NOT NULL AND (e.RecurrenceEndUtc IS NULL OR e.RecurrenceEndUtc > @From)));
        """;

    private static readonly string ExceptionsSql = $"""
        SELECT
        {EventColumns}
        {EventJoins}
        WHERE e.CompanyId = @TenantId
          AND e.GcRecord = 0
          AND e.SeriesMasterId = ANY(@Masters);
        """;

    private static readonly string ByIdSql = $"""
        SELECT
        {EventColumns},
        e.Description,
        e.RequesterPersonId,
        e.CustomerId,
        e.TicketId,
        e.ProjectId,
        e.StatusChangedUtc,
        e.StatusChangedByUserId,
        e.Created AS CreatedAt
        {EventJoins}
        WHERE e.CompanyId = @TenantId
          AND e.GcRecord = 0
          AND e.Id = @Id;
        """;

    private const string CalendarCompanySql = """
        SELECT cc.Id, cc.TimeZone, cc.IsOpen24Hours, cc.BusinessHoursStart, cc.BusinessHoursEnd
        FROM Calendar.CalendarCompanies cc
        WHERE cc.CompanyId = @TenantId
          AND cc.GcRecord = 0
        LIMIT 1;
        """;

    private const string HolidaysSql = """
        SELECT d.Id, d.Date, d.Name, d.IsRecurringYearly, d.Created AS CreatedAt
        FROM Calendar.CompanyNonWorkingDays d
        WHERE d.CompanyId = @TenantId
          AND d.GcRecord = 0
          AND (d.IsRecurringYearly = TRUE OR (d.Date >= @FromDate AND d.Date <= @ToDate));
        """;

    private const string PeriodsSql = """
        SELECT p.Id, p.UserId, p.StartDate, p.EndDate, p.Reason, p.Notes, p.Created AS CreatedAt
        FROM Calendar.UserNonWorkingPeriods p
        WHERE p.CompanyId = @TenantId
          AND p.GcRecord = 0
          AND p.UserId = ANY(@Owners)
          AND p.StartDate <= @ToDate
          AND p.EndDate >= @FromDate;
        """;

    /// <summary>
    /// Reads the time zone of the company's calendar configuration, or <c>null</c> when it is not configured.
    /// </summary>
    public async Task<CalendarCompanyRow?> GetCalendarCompanyAsync(IDbConnection connection, Guid companyId, CancellationToken cancellationToken) =>
        await connection.QueryFirstOrDefaultAsync<CalendarCompanyRow>(
            new CommandDefinition(CalendarCompanySql, new { TenantId = companyId }, cancellationToken: cancellationToken));

    /// <summary>
    /// Returns the expanded occurrences of the owners' activities in <c>[fromUtc, toUtc)</c>.
    /// </summary>
    public async Task<IReadOnlyList<CalendarOccurrenceDto>> GetOccurrencesAsync(
        IDbConnection connection,
        Guid companyId,
        IReadOnlyCollection<Guid> ownerUserIds,
        DateTime fromUtc,
        DateTime toUtc,
        string timeZoneId,
        CalendarOccurrenceFilter filter,
        CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", companyId);
        parameters.Add("Owners", ownerUserIds.ToArray());
        parameters.Add("From", CalendarTime.AsUtc(fromUtc));
        parameters.Add("To", CalendarTime.AsUtc(toUtc));

        var rows = (await connection.QueryAsync<CalendarEventRow>(
            new CommandDefinition(RangeSql, parameters, cancellationToken: cancellationToken))).AsList();

        var masterIds = rows.Where(r => r.RecurrenceRule is not null).Select(r => r.Id).ToArray();
        if (masterIds.Length > 0)
        {
            var exceptions = await connection.QueryAsync<CalendarEventRow>(
                new CommandDefinition(ExceptionsSql, new { TenantId = companyId, Masters = masterIds }, cancellationToken: cancellationToken));
            rows.AddRange(exceptions);
        }

        return CalendarOccurrenceExpansion.Expand(rows, fromUtc, toUtc, timeZoneId, expander)
            .Where(o => filter.IncludeCancelled || o.Source.StatusCode != CalendarCodes.Statuses.Cancelled)
            .Where(o => filter.CalendarIds is null || filter.CalendarIds.Count == 0 || filter.CalendarIds.Contains(o.Source.CalendarId))
            .Where(o => filter.ActivityCompanyIds is null || filter.ActivityCompanyIds.Count == 0 || filter.ActivityCompanyIds.Contains(o.Source.ActivityCompanyId))
            .Where(o => filter.StatusCompanyIds is null || filter.StatusCompanyIds.Count == 0 || filter.StatusCompanyIds.Contains(o.Source.StatusCompanyId))
            .Where(o => filter.ExcludeFinal is false || !o.Source.IsFinal)
            .Select(o => ToOccurrence(o, timeZoneId))
            .ToList();
    }

    /// <summary>
    /// Returns one activity row by id (tenant-scoped), or <c>null</c>.
    /// </summary>
    public async Task<CalendarEventDetailRow?> GetEventAsync(IDbConnection connection, Guid companyId, Guid eventId, CancellationToken cancellationToken) =>
        await connection.QueryFirstOrDefaultAsync<CalendarEventDetailRow>(
            new CommandDefinition(ByIdSql, new { TenantId = companyId, Id = eventId }, cancellationToken: cancellationToken));

    /// <summary>
    /// Holidays and non-working periods of a range. The periods of <paramref name="viewerUserId"/> keep their reason
    /// (<c>NonWorkingPeriod</c>); the periods of other users are only <c>Unavailable</c>, without reason.
    /// </summary>
    public async Task<IReadOnlyList<CalendarNonWorkingRangeDto>> GetNonWorkingDaysAsync(
        IDbConnection connection,
        Guid companyId,
        IReadOnlyCollection<Guid> ownerUserIds,
        Guid? viewerUserId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var holidays = await GetHolidaysAsync(connection, companyId, fromDate, toDate, cancellationToken);
        var periods = await GetPeriodsAsync(connection, companyId, ownerUserIds, fromDate, toDate, cancellationToken);

        var result = new List<CalendarNonWorkingRangeDto>();
        result.AddRange(holidays.Select(h => new CalendarNonWorkingRangeDto("Holiday", h.OccursOn, h.OccursOn, h.Name, null)));
        result.AddRange(periods.Select(p => p.UserId == viewerUserId
            ? new CalendarNonWorkingRangeDto("NonWorkingPeriod", p.StartDate, p.EndDate, ((CalendarNonWorkingReason)p.Reason).ToString(), p.UserId)
            : new CalendarNonWorkingRangeDto("Unavailable", p.StartDate, p.EndDate, null, p.UserId)));

        return result.OrderBy(x => x.StartDate).ThenBy(x => x.Kind).ToList();
    }

    /// <summary>
    /// Company holidays that fall in <c>[fromDate, toDate]</c>, recurring ones resolved for each year of the range.
    /// </summary>
    public async Task<IReadOnlyList<CalendarCompanyNonWorkingDayDto>> GetHolidaysAsync(
        IDbConnection connection,
        Guid companyId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<CalendarCompanyNonWorkingDayDto>(
            new CommandDefinition(HolidaysSql, new
            {
                TenantId = companyId,
                FromDate = fromDate.ToDateTime(TimeOnly.MinValue),
                ToDate = toDate.ToDateTime(TimeOnly.MinValue)
            }, cancellationToken: cancellationToken));

        var result = new List<CalendarCompanyNonWorkingDayDto>();
        foreach (var row in rows)
        {
            if (!row.IsRecurringYearly)
            {
                result.Add(row with { OccursOn = row.Date });
                continue;
            }

            for (var year = fromDate.Year; year <= toDate.Year; year++)
            {
                if (row.Date.Month == 2 && row.Date.Day == 29 && !DateTime.IsLeapYear(year))
                {
                    continue;
                }

                var occursOn = new DateOnly(year, row.Date.Month, row.Date.Day);
                if (occursOn >= fromDate && occursOn <= toDate)
                {
                    result.Add(row with { OccursOn = occursOn });
                }
            }
        }

        return result.OrderBy(x => x.OccursOn).ToList();
    }

    /// <summary>
    /// Non-working periods of the users that overlap <c>[fromDate, toDate]</c>.
    /// </summary>
    public async Task<IReadOnlyList<CalendarUserNonWorkingPeriodDto>> GetPeriodsAsync(
        IDbConnection connection,
        Guid companyId,
        IReadOnlyCollection<Guid> userIds,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken) =>
        (await connection.QueryAsync<CalendarUserNonWorkingPeriodDto>(
            new CommandDefinition(PeriodsSql, new
            {
                TenantId = companyId,
                Owners = userIds.ToArray(),
                FromDate = fromDate.ToDateTime(TimeOnly.MinValue),
                ToDate = toDate.ToDateTime(TimeOnly.MinValue)
            }, cancellationToken: cancellationToken))).AsList();

    /// <summary>
    /// Maps an expanded row to the view DTO.
    /// </summary>
    public static CalendarOccurrenceDto ToOccurrence(ExpandedOccurrence<CalendarEventRow> occurrence, string timeZoneId)
    {
        var row = occurrence.Source;
        return new CalendarOccurrenceDto
        {
            EventId = occurrence.EventId,
            ExceptionId = occurrence.ExceptionId,
            OccurrenceStartUtc = occurrence.OccurrenceStartUtc,
            StartUtc = occurrence.StartUtc,
            EndUtc = occurrence.EndUtc,
            IsAllDay = row.IsAllDay,
            IsRecurring = occurrence.IsRecurring,
            Title = row.Title,
            Summary = row.Summary,
            Location = row.Location,
            VideoCallUrl = row.VideoCallUrl,
            CalendarId = row.CalendarId,
            CalendarName = row.CalendarName,
            CalendarColor = row.CalendarColor,
            Activity = new CalendarActivityRefDto(row.ActivityCompanyId, row.ActivityCode, row.ActivityName, row.ActivityColor, row.AllowsOverlap),
            Status = new CalendarStatusRefDto(row.StatusCompanyId, row.StatusCode, row.StatusName, row.StatusColor, row.BlocksTime, row.IsFinal),
            OwnerUserId = row.OwnerUserId,
            OwnerName = row.OwnerName.Trim(),
            Origin = new CalendarOriginDto(row.ChannelCode, ((CalendarEventOriginType)row.OriginType).ToString(), row.OriginIdentifier, row.OriginUserId),
            TimeZone = timeZoneId
        };
    }
}

/// <summary>
/// Filters applied after the expansion.
/// </summary>
public sealed record CalendarOccurrenceFilter(
    IReadOnlyCollection<Guid>? CalendarIds = null,
    IReadOnlyCollection<Guid>? ActivityCompanyIds = null,
    IReadOnlyCollection<Guid>? StatusCompanyIds = null,
    bool IncludeCancelled = false,
    bool ExcludeFinal = false);

/// <summary>
/// Calendar configuration columns needed by the queries.
/// </summary>
public sealed record CalendarCompanyRow
{
    public Guid Id { get; init; }
    public string TimeZone { get; init; } = string.Empty;
    public bool IsOpen24Hours { get; init; }
    public TimeOnly? BusinessHoursStart { get; init; }
    public TimeOnly? BusinessHoursEnd { get; init; }
}

/// <summary>
/// One activity row read by Dapper for the views.
/// </summary>
public record CalendarEventRow : ICalendarEventSlot
{
    public Guid Id { get; init; }
    public Guid? SeriesMasterId { get; init; }
    public DateTime? OriginalStartUtc { get; init; }
    public Guid CalendarId { get; init; }
    public string CalendarName { get; init; } = string.Empty;
    public string CalendarColor { get; init; } = string.Empty;
    public Guid OwnerUserId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string? Location { get; init; }
    public string? VideoCallUrl { get; init; }
    public bool IsAllDay { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public string? RecurrenceRule { get; init; }
    public DateTime? RecurrenceEndUtc { get; init; }
    public Guid ActivityCompanyId { get; init; }
    public string ActivityCode { get; init; } = string.Empty;
    public string ActivityName { get; init; } = string.Empty;
    public string ActivityColor { get; init; } = string.Empty;
    public bool AllowsOverlap { get; init; }
    public Guid StatusCompanyId { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public string StatusName { get; init; } = string.Empty;
    public string StatusColor { get; init; } = string.Empty;
    public bool BlocksTime { get; init; }
    public bool IsFinal { get; init; }
    public string ChannelCode { get; init; } = string.Empty;
    public int OriginType { get; init; }
    public string? OriginIdentifier { get; init; }
    public Guid? OriginUserId { get; init; }
}

/// <summary>
/// Activity row with the detail columns.
/// </summary>
public sealed record CalendarEventDetailRow : CalendarEventRow
{
    public string? Description { get; init; }
    public Guid? RequesterPersonId { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? TicketId { get; init; }
    public Guid? ProjectId { get; init; }
    public DateTime? StatusChangedUtc { get; init; }
    public Guid? StatusChangedByUserId { get; init; }
    public DateTime CreatedAt { get; init; }
}
