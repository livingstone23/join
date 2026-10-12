using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.GetIntakePersonEvents;

/// <summary>
/// Lists the non-cancelled activities of a person in the company. The agent uses this to pick which activity to
/// reschedule or cancel (SPEC 45). The series expansion is the panel pattern: pull one-time and master rows,
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
               e.Title,
               s.Code AS StatusCode,
               u.FirstName || ' ' || u.LastName AS OwnerName
          FROM Calendar.Events e
          INNER JOIN Calendar.StatusCompanies sc ON sc.Id = e.CalendarStatusCompanyId
          INNER JOIN Calendar.Statuses s ON s.Id = sc.CalendarStatusId
          INNER JOIN Security.Users u ON u.Id = e.OwnerUserId
         WHERE e.CompanyId = @TenantId
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
               e.Title,
               s.Code AS StatusCode,
               u.FirstName || ' ' || u.LastName AS OwnerName
          FROM Calendar.Events e
          INNER JOIN Calendar.StatusCompanies sc ON sc.Id = e.CalendarStatusCompanyId
          INNER JOIN Calendar.Statuses s ON s.Id = sc.CalendarStatusId
          INNER JOIN Security.Users u ON u.Id = e.OwnerUserId
         WHERE e.CompanyId = @TenantId
           AND e.GcRecord = 0
           AND e.SeriesMasterId = ANY(@Masters);
        """;

    public async Task<Response<IReadOnlyList<IntakeEventDto>>> Handle(GetIntakePersonEventsQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<IReadOnlyList<IntakeEventDto>>.Error(CalendarErrorCodes.CompanyRequired,
                ["The X-Company-Id header or the CompanyId claim is required."]);
        }

        var fromUtc = (request.From ?? DateTimeOffset.UtcNow.Date).UtcDateTime;
        var toUtc = (request.To ?? DateTimeOffset.UtcNow.AddDays(MaxDays)).UtcDateTime;
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
            new CommandDefinition(RangeSql, new { TenantId = companyId, RequesterPersonId = request.RequesterPersonId, From = fromUtc, To = toUtc }, cancellationToken: cancellationToken)))
            .AsList();

        var masterIds = rows.Where(r => r.RecurrenceRule is not null).Select(r => r.Id).ToArray();
        if (masterIds.Length > 0)
        {
            var exceptions = await connection.QueryAsync<Row>(
                new CommandDefinition(ExceptionsSql, new { TenantId = companyId, Masters = masterIds }, cancellationToken: cancellationToken));
            rows.AddRange(exceptions);
        }

        var expanded = CalendarOccurrenceExpansion.Expand(rows, fromUtc, toUtc, company.TimeZone, expander)
            .Where(o => o.Source.StatusCode != CalendarCodes.Statuses.Cancelled)
            .OrderBy(o => o.StartUtc)
            .ToList();

        var result = new List<IntakeEventDto>(expanded.Count);
        foreach (var occurrence in expanded)
        {
            var row = occurrence.Source;
            var statusCompany = await LoadStatusCompanyAsync(connection, companyId, row.StatusCode, cancellationToken);

            result.Add(new IntakeEventDto(
                EventId: occurrence.EventId,
                OccurrenceStartUtc: occurrence.OccurrenceStartUtc,
                Title: row.Title,
                StartUtc: occurrence.StartUtc,
                EndUtc: occurrence.EndUtc,
                Status: new IntakeStatusDto(statusCompany.Id, row.StatusCode, statusCompany.Name, statusCompany.Color),
                OwnerFullName: row.OwnerName.Trim()));
        }

        return new Response<IReadOnlyList<IntakeEventDto>>
        {
            IsSuccess = true,
            Data = result,
            Message = "Intake person events retrieved successfully."
        };
    }

    private static async Task<(Guid Id, string Name, string Color)> LoadStatusCompanyAsync(
        System.Data.IDbConnection connection, Guid companyId, string code, CancellationToken cancellationToken)
    {
        var status = await connection.QueryFirstOrDefaultAsync<StatusRow>(
            new CommandDefinition("""
                SELECT sc.Id, sc.Name, sc.Color
                  FROM Calendar.StatusCompanies sc
                  INNER JOIN Calendar.Statuses s ON s.Id = sc.CalendarStatusId
                 WHERE sc.CompanyId = @TenantId AND sc.GcRecord = 0 AND sc.IsActive = TRUE
                   AND s.GcRecord = 0 AND s.IsActive = TRUE
                   AND s.Code = @Code
                 LIMIT 1;
                """, new { TenantId = companyId, Code = code }, cancellationToken: cancellationToken));
        return status is null ? (Guid.Empty, string.Empty, string.Empty) : (status.Id, status.Name, status.Color);
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
        public string StatusCode { get; init; } = string.Empty;
        public string OwnerName { get; init; } = string.Empty;
    }

    private sealed class StatusRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Color { get; init; } = string.Empty;
    }
}