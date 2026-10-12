using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.GetIntakePersonEvents;

/// <summary>
/// Lists the non-cancelled activities of a person in the company, without the internal <c>PERSON_REVIEW</c>
/// activity. The agent uses this to pick which activity to reschedule or cancel (SPEC 45). The series expansion is the panel pattern: pull one-time and master rows,
/// add their exceptions, expand with <see cref="ICalendarRecurrenceExpander"/>, filter by
/// <c>RequesterPersonId</c> and exclude <c>CANCELLED</c>.
/// </summary>
public sealed class GetIntakePersonEventsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICalendarRecurrenceExpander expander,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetIntakePersonEventsQuery, Response<IReadOnlyList<IntakeEventDto>>>
{
    /// <summary> Maximum range the agent may ask for (SPEC 45). </summary>
    public const int MaxDays = 90;

    private const string RangeSql = """
        SELECT e.Id,
               e.SeriesMasterId,
               e.OriginalStartUtc,
               e.StartUtc,
               e.EndUtc,
               e.RecurrenceRule,
               e.RecurrenceEndUtc,
               e.IsAllDay,
               e.Title,
               sc.Id AS StatusCompanyId,
               s.Code AS StatusCode,
               sc.Name AS StatusName,
               sc.Color AS StatusColor,
               TRIM(CONCAT(u.FirstName, ' ', COALESCE(u.LastName, ''))) AS OwnerName
          FROM Calendar.Events e
          INNER JOIN Calendar.StatusCompanies sc ON sc.Id = e.CalendarStatusCompanyId
          INNER JOIN Calendar.Statuses s ON s.Id = sc.CalendarStatusId
          INNER JOIN Security.Users u ON u.Id = e.OwnerUserId
          INNER JOIN Calendar.ActivityCompanies ac ON ac.Id = e.CalendarActivityCompanyId
          INNER JOIN Calendar.Activities a ON a.Id = ac.CalendarActivityId
         WHERE e.CompanyId = @TenantId
           AND a.Code <> @PersonReview
           AND e.GcRecord = 0
           AND e.SeriesMasterId IS NULL
           AND e.RequesterPersonId = @RequesterPersonId
           AND e.StartUtc < @To
           AND ((e.RecurrenceRule IS NULL AND e.EndUtc > @From)
                OR (e.RecurrenceRule IS NOT NULL AND (e.RecurrenceEndUtc IS NULL OR e.RecurrenceEndUtc > @From)));
        """;

    private const string ExceptionsSql = """
        SELECT e.Id,
               e.SeriesMasterId,
               e.OriginalStartUtc,
               e.StartUtc,
               e.EndUtc,
               e.RecurrenceRule,
               e.RecurrenceEndUtc,
               e.IsAllDay,
               e.Title,
               sc.Id AS StatusCompanyId,
               s.Code AS StatusCode,
               sc.Name AS StatusName,
               sc.Color AS StatusColor,
               TRIM(CONCAT(u.FirstName, ' ', COALESCE(u.LastName, ''))) AS OwnerName
          FROM Calendar.Events e
          INNER JOIN Calendar.StatusCompanies sc ON sc.Id = e.CalendarStatusCompanyId
          INNER JOIN Calendar.Statuses s ON s.Id = sc.CalendarStatusId
          INNER JOIN Security.Users u ON u.Id = e.OwnerUserId
          INNER JOIN Calendar.ActivityCompanies ac ON ac.Id = e.CalendarActivityCompanyId
          INNER JOIN Calendar.Activities a ON a.Id = ac.CalendarActivityId
         WHERE e.CompanyId = @TenantId
           AND a.Code <> @PersonReview
           AND e.GcRecord = 0
           AND e.SeriesMasterId = ANY(@Masters);
        """;

    public async Task<Response<IReadOnlyList<IntakeEventDto>>> Handle(GetIntakePersonEventsQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<IReadOnlyList<IntakeEventDto>>.Error(CalendarErrorCodes.CompanyRequired,
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var fromUtc = (request.From ?? new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero)).UtcDateTime;
        var toUtc = request.To?.UtcDateTime ?? fromUtc.AddDays(MaxDays);
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(MaxDays))
        {
            return Response<IReadOnlyList<IntakeEventDto>>.Error(CalendarErrorCodes.RangeTooLarge,
                [$"'to' must be later than 'from' and the range cannot exceed {MaxDays} days."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var company = await connection.QueryFirstOrDefaultAsync<CompanyZoneRow>(
            new CommandDefinition("SELECT cc.TimeZone FROM Calendar.CalendarCompanies cc WHERE cc.CompanyId = @TenantId AND cc.GcRecord = 0 LIMIT 1",
                new { TenantId = companyId }, cancellationToken: cancellationToken));
        if (company is null)
        {
            return Response<IReadOnlyList<IntakeEventDto>>.Error(CalendarErrorCodes.CompanyNotConfigured,
                ["The company has no calendar configuration."]);
        }

        var rows = (await connection.QueryAsync<Row>(
            new CommandDefinition(RangeSql, new { TenantId = companyId, RequesterPersonId = request.RequesterPersonId, From = fromUtc, To = toUtc, PersonReview = CalendarCodes.Activities.PersonReview }, cancellationToken: cancellationToken)))
            .AsList();

        var masterIds = rows.Where(r => r.RecurrenceRule is not null).Select(r => r.Id).ToArray();
        if (masterIds.Length > 0)
        {
            var exceptions = await connection.QueryAsync<Row>(
                new CommandDefinition(ExceptionsSql, new { TenantId = companyId, Masters = masterIds, PersonReview = CalendarCodes.Activities.PersonReview }, cancellationToken: cancellationToken));
            rows.AddRange(exceptions);
        }

        var expanded = CalendarOccurrenceExpansion.Expand(rows, fromUtc, toUtc, company.TimeZone, expander)
            .Where(o => o.Source.StatusCode != CalendarCodes.Statuses.Cancelled)
            .OrderBy(o => o.StartUtc)
            .ToList();

        var result = expanded
            .Select(o => new IntakeEventDto(
                o.EventId,
                o.OccurrenceStartUtc,
                o.Source.Title,
                o.StartUtc,
                o.EndUtc,
                new IntakeStatusDto(o.Source.StatusCompanyId, o.Source.StatusCode, o.Source.StatusName, o.Source.StatusColor),
                o.Source.OwnerName.Trim()))
            .ToList();

        return new Response<IReadOnlyList<IntakeEventDto>>
        {
            IsSuccess = true,
            Data = result,
            Message = "Intake person events retrieved successfully."
        };
    }

    private sealed class CompanyZoneRow
    {
        public string TimeZone { get; init; } = string.Empty;
    }

    private sealed class Row : ICalendarEventSlot
    {
        public Guid Id { get; init; }
        public Guid? SeriesMasterId { get; init; }
        public DateTime? OriginalStartUtc { get; init; }
        public DateTime StartUtc { get; init; }
        public DateTime EndUtc { get; init; }
        public bool IsAllDay { get; init; }
        public string? RecurrenceRule { get; init; }
        public DateTime? RecurrenceEndUtc { get; init; }
        public string Title { get; init; } = string.Empty;
        public Guid StatusCompanyId { get; init; }
        public string StatusCode { get; init; } = string.Empty;
        public string StatusName { get; init; } = string.Empty;
        public string StatusColor { get; init; } = string.Empty;
        public string OwnerName { get; init; } = string.Empty;
    }
}