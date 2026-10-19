using System.Data;
using Dapper;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityIndex;

/// <summary>
/// SPEC 68 — Dapper read of the activity index, abstracted so the query handler can be tested with a fake
/// reader. The default implementation returns the one-time activities and series masters of the range plus every
/// exception of those masters, so <see cref="CalendarOccurrenceExpansion"/> can fold them.
/// </summary>
public interface ICalendarActivityIndexReader
{
    Task<IReadOnlyList<CalendarActivityIndexRow>> ReadAsync(
        IDbConnection connection,
        CalendarActivityIndexCriteria criteria,
        CancellationToken cancellationToken);
}

/// <summary>
/// Dapper implementation of <see cref="ICalendarActivityIndexReader"/>. Same range rule as "Mi calendario"
/// (<c>CalendarQueryReader.RangeSql</c>): a series is a candidate while <c>RecurrenceEndUtc</c> reaches the range,
/// not only when its first occurrence does. The attribute filters (type, status, channel, person, text) only
/// narrow one-time activities in SQL; series masters and their exceptions always come back, because an
/// exception that fails a filter must still hide the master occurrence it replaces. The handler re-applies every
/// filter after the expansion.
/// </summary>
public sealed class CalendarActivityIndexReader : ICalendarActivityIndexReader
{
    private const string Columns = """
        e.Id,
        e.SeriesMasterId,
        e.OriginalStartUtc,
        e.IsAllDay,
        e.StartUtc,
        e.EndUtc,
        e.RecurrenceRule,
        e.Title,
        e.Summary,
        e.Location,
        e.CalendarActivityCompanyId AS ActivityCompanyId,
        act.Name AS ActivityName,
        act.Color AS ActivityColor,
        e.CalendarStatusCompanyId AS StatusCompanyId,
        s.Code AS StatusCode,
        sc.Name AS StatusName,
        sc.Color AS StatusColor,
        e.OwnerUserId,
        CONCAT(u.FirstName, ' ', u.LastName) AS OwnerName,
        e.CalendarId,
        cal.Name AS CalendarName,
        e.RequesterPersonId,
        NULLIF(CASE
                    WHEN p.PersonType = 2 AND NULLIF(p.CommercialName, '') IS NOT NULL THEN p.CommercialName
                    ELSE CONCAT_WS(' ', NULLIF(p.FirstName, ''), NULLIF(p.MiddleName, ''), NULLIF(p.LastName, ''), NULLIF(p.SecondLastName, ''))
                END, '') AS RequesterPersonName,
        cust.CustomerCode AS CustomerCode,
        e.OriginChannelId,
        ch.Code AS OriginChannelCode
        FROM Calendar.Events e
        INNER JOIN Security.Users u ON u.Id = e.OwnerUserId
        INNER JOIN Calendar.UserCalendars cal ON cal.Id = e.CalendarId
        INNER JOIN Calendar.ActivityCompanies act ON act.Id = e.CalendarActivityCompanyId
        INNER JOIN Calendar.StatusCompanies sc ON sc.Id = e.CalendarStatusCompanyId
        INNER JOIN Calendar.Statuses s ON s.Id = sc.CalendarStatusId
        INNER JOIN Common.CommunicationChannels ch ON ch.Id = e.OriginChannelId
        LEFT JOIN Admin.Persons p
            ON p.Id = e.RequesterPersonId
           AND p.CompanyId = e.CompanyId
           AND p.GcRecord = 0
        LEFT JOIN Customer.Customers cust
            ON cust.Id = e.CustomerId
           AND cust.CompanyId = e.CompanyId
           AND cust.GcRecord = 0
        """;

    private const string ExceptionsSql = $"""
        SELECT
        {Columns}
        WHERE e.CompanyId = @CompanyId
          AND e.GcRecord = 0
          AND e.SeriesMasterId = ANY(@Masters);
        """;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarActivityIndexRow>> ReadAsync(
        IDbConnection connection,
        CalendarActivityIndexCriteria criteria,
        CancellationToken cancellationToken)
    {
        var clauses = new List<string>
        {
            "e.CompanyId = @CompanyId",
            "e.GcRecord = 0",
            "e.SeriesMasterId IS NULL",
            "e.StartUtc < @ToUtc",
            @"((e.RecurrenceRule IS NULL AND e.EndUtc > @FromUtc)
              OR (e.RecurrenceRule IS NOT NULL AND (e.RecurrenceEndUtc IS NULL OR e.RecurrenceEndUtc > @FromUtc)))"
        };

        if (criteria.OwnerUserIds is { Count: > 0 })
        {
            clauses.Add("e.OwnerUserId = ANY(@OwnerUserIds)");
        }

        if (criteria.CalendarIds is { Count: > 0 })
        {
            clauses.Add("e.CalendarId = ANY(@CalendarIds)");
        }

        // Attribute filters only prune one-time activities; a series is filtered per occurrence by the handler.
        var oneTimeFilters = new List<string>();
        if (criteria.ActivityCompanyIds is { Count: > 0 })
        {
            oneTimeFilters.Add("e.CalendarActivityCompanyId = ANY(@ActivityCompanyIds)");
        }

        if (criteria.StatusCompanyIds is { Count: > 0 })
        {
            oneTimeFilters.Add("e.CalendarStatusCompanyId = ANY(@StatusCompanyIds)");
        }

        if (criteria.OriginChannelIds is { Count: > 0 })
        {
            oneTimeFilters.Add("e.OriginChannelId = ANY(@OriginChannelIds)");
        }

        if (criteria.RequesterPersonId is not null)
        {
            oneTimeFilters.Add("e.RequesterPersonId = @RequesterPersonId");
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            oneTimeFilters.Add("(e.Title ILIKE @Search OR COALESCE(e.Summary, '') ILIKE @Search OR COALESCE(e.Location, '') ILIKE @Search)");
        }

        if (oneTimeFilters.Count > 0)
        {
            clauses.Add($"(e.RecurrenceRule IS NOT NULL OR ({string.Join(" AND ", oneTimeFilters)}))");
        }

        var sql = $"""
            SELECT
            {Columns}
            WHERE {string.Join(" AND ", clauses)};
            """;

        var rows = (await connection.QueryAsync<CalendarActivityIndexRow>(
            new CommandDefinition(sql, new
            {
                CompanyId = criteria.CompanyId,
                FromUtc = CalendarTime.AsUtc(criteria.FromUtc),
                ToUtc = CalendarTime.AsUtc(criteria.ToUtc),
                OwnerUserIds = criteria.OwnerUserIds?.ToArray(),
                CalendarIds = criteria.CalendarIds?.ToArray(),
                ActivityCompanyIds = criteria.ActivityCompanyIds?.ToArray(),
                StatusCompanyIds = criteria.StatusCompanyIds?.ToArray(),
                OriginChannelIds = criteria.OriginChannelIds?.ToArray(),
                RequesterPersonId = criteria.RequesterPersonId,
                Search = string.IsNullOrWhiteSpace(criteria.Search) ? null : $"%{EscapeLikePattern(criteria.Search.Trim())}%"
            }, cancellationToken: cancellationToken))).AsList();

        var masterIds = rows.Where(r => r.RecurrenceRule is not null).Select(r => r.Id).ToArray();
        if (masterIds.Length > 0)
        {
            var exceptions = await connection.QueryAsync<CalendarActivityIndexRow>(
                new CommandDefinition(ExceptionsSql, new { CompanyId = criteria.CompanyId, Masters = masterIds },
                    cancellationToken: cancellationToken));
            rows.AddRange(exceptions);
        }

        return rows;
    }

    /// <summary>
    /// Escapes <c>\</c>, <c>%</c> and <c>_</c> so the user's text matches literally (PostgreSQL's default LIKE
    /// escape is <c>\</c>).
    /// </summary>
    public static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}