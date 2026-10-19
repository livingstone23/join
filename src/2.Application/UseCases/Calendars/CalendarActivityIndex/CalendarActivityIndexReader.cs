using System.Data;
using Dapper;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityIndex;

/// <summary>
/// SPEC 68 — Dapper read of the activity index, abstracted so the query handler can be tested with a fake
/// reader. The default implementation composes the SQL with optional filters and returns master + exception
/// rows in a single pass so <see cref="CalendarOccurrenceExpansion"/> can fold them.
/// </summary>
public interface ICalendarActivityIndexReader
{
    Task<IReadOnlyList<CalendarActivityIndexRow>> ReadAsync(
        IDbConnection connection,
        CalendarActivityIndexCriteria criteria,
        CancellationToken cancellationToken);
}

/// <summary>
/// Dapper implementation of <see cref="ICalendarActivityIndexReader"/>.
/// </summary>
public sealed class CalendarActivityIndexReader : ICalendarActivityIndexReader
{
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
            @"((e.SeriesMasterId IS NULL AND e.StartUtc < @ToUtc AND e.EndUtc > @FromUtc)
              OR (e.SeriesMasterId IS NOT NULL AND e.OriginalStartUtc >= @FromUtc AND e.OriginalStartUtc < @ToUtc))"
        };

        if (criteria.OwnerUserIds is { Count: > 0 })
        {
            clauses.Add("e.OwnerUserId = ANY(@OwnerUserIds)");
        }

        if (criteria.CalendarIds is { Count: > 0 })
        {
            clauses.Add("e.CalendarId = ANY(@CalendarIds)");
        }

        if (criteria.ActivityCompanyIds is { Count: > 0 })
        {
            clauses.Add("e.CalendarActivityCompanyId = ANY(@ActivityCompanyIds)");
        }

        if (criteria.StatusCompanyIds is { Count: > 0 })
        {
            clauses.Add("e.CalendarStatusCompanyId = ANY(@StatusCompanyIds)");
        }

        if (criteria.OriginChannelIds is { Count: > 0 })
        {
            clauses.Add("e.OriginChannelId = ANY(@OriginChannelIds)");
        }

        if (criteria.RequesterPersonId is { } personId)
        {
            clauses.Add("e.RequesterPersonId = @RequesterPersonId");
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            clauses.Add("(e.Title ILIKE @Search OR COALESCE(e.Summary, '') ILIKE @Search OR COALESCE(e.Location, '') ILIKE @Search)");
        }

        var where = string.Join(" AND ", clauses);
        var sql = $@"
            SELECT
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
            WHERE {where}
            ORDER BY e.Id;";

        var rows = await connection.QueryAsync<CalendarActivityIndexRow>(
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
                Search = string.IsNullOrWhiteSpace(criteria.Search) ? null : $"%{EscapeLikePattern(criteria.Search)}%"
            }, cancellationToken: cancellationToken));

        return rows.AsList();
    }

    /// <summary>
    /// Escapes <c>\</c>, <c>%</c> and <c>_</c> so the user's text matches literally (PostgreSQL's default LIKE
    /// escape is <c>\</c>).
    /// </summary>
    public static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}