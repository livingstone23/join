using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Read side shared by <c>CalendarEvents</c> (owner = current user, R2) and <c>CalendarPanel</c> (any user of the
/// company): range of occurrences, detail and log. Dapper only; every query is scoped by the tenant.
/// </summary>
public sealed class CalendarEventQueryService(
    ISqlConnectionFactory connectionFactory,
    CalendarQueryReader reader,
    ICurrentUserService currentUserService)
{
    private const string LogsSql = """
        SELECT l.Id, l.CalendarEventId, l.Action, l.PreviousStartUtc, l.PreviousEndUtc, l.NewStartUtc, l.NewEndUtc,
               l.PreviousStatusCompanyId, psc.Name AS PreviousStatusName,
               l.NewStatusCompanyId, nsc.Name AS NewStatusName,
               ch.Code AS ChannelCode, l.ActorUserId, CONCAT(u.FirstName, ' ', u.LastName) AS ActorName,
               l.ActorIdentifier, l.Notes, l.OccurredUtc,
               l.CalendarEventCommentId AS CommentId, l.CalendarEventDocumentId AS DocumentId
        FROM Calendar.EventLogs l
        INNER JOIN Common.CommunicationChannels ch ON ch.Id = l.ChannelId
        LEFT JOIN Calendar.StatusCompanies psc ON psc.Id = l.PreviousStatusCompanyId
        LEFT JOIN Calendar.StatusCompanies nsc ON nsc.Id = l.NewStatusCompanyId
        LEFT JOIN Security.Users u ON u.Id = l.ActorUserId
        WHERE l.CompanyId = @TenantId
          AND l.GcRecord = 0
          AND (l.CalendarEventId = @Id
               OR l.CalendarEventId IN (SELECT e.Id FROM Calendar.Events e WHERE e.CompanyId = @TenantId AND e.SeriesMasterId = @Id))
        ORDER BY l.OccurredUtc, l.Created;
        """;

    /// <summary>
    /// Resolves the tenant and the current user.
    /// </summary>
    public Response<(Guid CompanyId, Guid UserId)> Open()
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<(Guid, Guid)>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        return Guid.TryParse(currentUserService.UserId, out var userId)
            ? new Response<(Guid, Guid)> { IsSuccess = true, Data = (currentUserService.CompanyId, userId) }
            : Response<(Guid, Guid)>.Error(CalendarErrorCodes.UserRequired, ["The authenticated user could not be resolved."]);
    }

    /// <summary>
    /// Occurrences of the owners in <c>[fromUtc, toUtc)</c> plus holidays and non-working periods
    /// (the viewer's own with reason, the others' as <c>Unavailable</c>). Range at most 62 days.
    /// </summary>
    public async Task<Response<CalendarEventRangeDto>> GetRangeAsync(
        IReadOnlyCollection<Guid> ownerUserIds,
        DateTime fromUtc,
        DateTime toUtc,
        CalendarOccurrenceFilter filter,
        CancellationToken cancellationToken,
        int maxDays = CalendarQueryReader.MaxRangeDays)
    {
        var opened = Open();
        if (!opened.IsSuccess)
        {
            return Response<CalendarEventRangeDto>.Error(opened.Message, opened.Errors);
        }

        var (companyId, viewerId) = opened.Data;
        fromUtc = CalendarTime.AsUtc(fromUtc);
        toUtc = CalendarTime.AsUtc(toUtc);
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(maxDays))
        {
            return Response<CalendarEventRangeDto>.Error(CalendarErrorCodes.RangeTooLarge, [$"'to' must be later than 'from' and the range cannot exceed {maxDays} days."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var company = await reader.GetCalendarCompanyAsync(connection, companyId, cancellationToken);
        if (company is null)
        {
            return Response<CalendarEventRangeDto>.Error(CalendarErrorCodes.CompanyNotConfigured, ["The company has no calendar configuration."]);
        }

        var zone = CalendarTime.FindTimeZone(company.TimeZone) ?? TimeZoneInfo.Utc;
        var occurrences = await reader.GetOccurrencesAsync(connection, companyId, ownerUserIds, fromUtc, toUtc, company.TimeZone, filter, cancellationToken);
        var nonWorking = await reader.GetNonWorkingDaysAsync(connection, companyId, ownerUserIds, viewerId,
            CalendarTime.LocalDate(fromUtc, zone), CalendarTime.LocalDate(toUtc.AddTicks(-1), zone), cancellationToken);

        return new Response<CalendarEventRangeDto>
        {
            IsSuccess = true,
            Message = "Calendar activities retrieved successfully.",
            Data = new CalendarEventRangeDto
            {
                TimeZone = company.TimeZone,
                From = fromUtc,
                To = toUtc,
                Occurrences = occurrences,
                NonWorkingDays = nonWorking
            }
        };
    }

    /// <summary>
    /// Detail of an activity. With <paramref name="ownOnly"/> another user's activity answers 404 (R2).
    /// </summary>
    public async Task<Response<CalendarEventDto>> GetByIdAsync(Guid eventId, bool ownOnly, CancellationToken cancellationToken)
    {
        var opened = Open();
        if (!opened.IsSuccess)
        {
            return Response<CalendarEventDto>.Error(opened.Message, opened.Errors);
        }

        var (companyId, userId) = opened.Data;
        using var connection = connectionFactory.CreateConnection();
        var company = await reader.GetCalendarCompanyAsync(connection, companyId, cancellationToken);
        if (company is null)
        {
            return Response<CalendarEventDto>.Error(CalendarErrorCodes.CompanyNotConfigured, ["The company has no calendar configuration."]);
        }

        var row = await reader.GetEventAsync(connection, companyId, eventId, cancellationToken);
        if (row is null || (ownOnly && row.OwnerUserId != userId))
        {
            return Response<CalendarEventDto>.Error(CalendarErrorCodes.EventNotFound, ["The calendar activity was not found."]);
        }

        return new Response<CalendarEventDto> { IsSuccess = true, Data = ToDto(row, company.TimeZone), Message = "Calendar activity retrieved successfully." };
    }

    /// <summary>
    /// Log of an activity and of its exceptions, oldest first.
    /// </summary>
    public async Task<Response<IReadOnlyList<CalendarEventLogDto>>> GetLogsAsync(Guid eventId, bool ownOnly, CancellationToken cancellationToken)
    {
        var opened = Open();
        if (!opened.IsSuccess)
        {
            return Response<IReadOnlyList<CalendarEventLogDto>>.Error(opened.Message, opened.Errors);
        }

        var (companyId, userId) = opened.Data;
        using var connection = connectionFactory.CreateConnection();
        var row = await reader.GetEventAsync(connection, companyId, eventId, cancellationToken);
        if (row is null || (ownOnly && row.OwnerUserId != userId))
        {
            return Response<IReadOnlyList<CalendarEventLogDto>>.Error(CalendarErrorCodes.EventNotFound, ["The calendar activity was not found."]);
        }

        var logs = (await connection.QueryAsync<CalendarEventLogRow>(
                new CommandDefinition(LogsSql, new { TenantId = companyId, Id = eventId }, cancellationToken: cancellationToken)))
            .Select(l => new CalendarEventLogDto
            {
                Id = l.Id,
                CalendarEventId = l.CalendarEventId,
                Action = ((CalendarEventLogAction)l.Action).ToString(),
                PreviousStartUtc = l.PreviousStartUtc,
                PreviousEndUtc = l.PreviousEndUtc,
                NewStartUtc = l.NewStartUtc,
                NewEndUtc = l.NewEndUtc,
                PreviousStatusCompanyId = l.PreviousStatusCompanyId,
                PreviousStatusName = l.PreviousStatusName,
                NewStatusCompanyId = l.NewStatusCompanyId,
                NewStatusName = l.NewStatusName,
                ChannelCode = l.ChannelCode,
                ActorUserId = l.ActorUserId,
                ActorName = l.ActorName?.Trim(),
                ActorIdentifier = l.ActorIdentifier,
                Notes = l.Notes,
                OccurredUtc = l.OccurredUtc,
                CommentId = l.CommentId,
                DocumentId = l.DocumentId
            })
            .ToList();

        return new Response<IReadOnlyList<CalendarEventLogDto>> { IsSuccess = true, Data = logs, Message = "Calendar activity log retrieved successfully." };
    }

    /// <summary>
    /// Maps a detail row to the activity DTO.
    /// </summary>
    public static CalendarEventDto ToDto(CalendarEventDetailRow row, string timeZone) => new()
    {
        Id = row.Id,
        CalendarId = row.CalendarId,
        CalendarName = row.CalendarName,
        CalendarColor = row.CalendarColor,
        OwnerUserId = row.OwnerUserId,
        OwnerName = row.OwnerName.Trim(),
        Activity = new CalendarActivityRefDto(row.ActivityCompanyId, row.ActivityCode, row.ActivityName, row.ActivityColor, row.AllowsOverlap),
        Status = new CalendarStatusRefDto(row.StatusCompanyId, row.StatusCode, row.StatusName, row.StatusColor, row.BlocksTime, row.IsFinal),
        Title = row.Title,
        Summary = row.Summary,
        Description = row.Description,
        Location = row.Location,
        VideoCallUrl = row.VideoCallUrl,
        IsAllDay = row.IsAllDay,
        StartUtc = CalendarTime.AsUtc(row.StartUtc),
        EndUtc = CalendarTime.AsUtc(row.EndUtc),
        IsRecurring = row.RecurrenceRule is not null || row.SeriesMasterId.HasValue,
        RecurrenceRule = row.RecurrenceRule,
        RecurrenceDescription = row.RecurrenceRule is null ? null : CalendarRecurrenceRule.Parse(row.RecurrenceRule).Data?.Describe(),
        RecurrenceEndUtc = row.RecurrenceEndUtc,
        SeriesMasterId = row.SeriesMasterId,
        OriginalStartUtc = row.OriginalStartUtc,
        Origin = new CalendarOriginDto(row.ChannelCode, ((CalendarEventOriginType)row.OriginType).ToString(), row.OriginIdentifier, row.OriginUserId),
        RequesterPersonId = row.RequesterPersonId,
        RequesterPersonName = row.RequesterPersonName,
        CustomerId = row.CustomerId,
        CustomerCode = row.CustomerCode,
        IsCustomerActive = row.IsCustomerActive,
        TicketId = row.TicketId,
        ProjectId = row.ProjectId,
        StatusChangedUtc = row.StatusChangedUtc,
        StatusChangedByUserId = row.StatusChangedByUserId,
        TimeZone = timeZone,
        CreatedAt = row.CreatedAt
    };

    private sealed record CalendarEventLogRow
    {
        public Guid Id { get; init; }
        public Guid CalendarEventId { get; init; }
        public int Action { get; init; }
        public DateTime? PreviousStartUtc { get; init; }
        public DateTime? PreviousEndUtc { get; init; }
        public DateTime? NewStartUtc { get; init; }
        public DateTime? NewEndUtc { get; init; }
        public Guid? PreviousStatusCompanyId { get; init; }
        public string? PreviousStatusName { get; init; }
        public Guid? NewStatusCompanyId { get; init; }
        public string? NewStatusName { get; init; }
        public string ChannelCode { get; init; } = string.Empty;
        public Guid? ActorUserId { get; init; }
        public string? ActorName { get; init; }
        public string? ActorIdentifier { get; init; }
        public string? Notes { get; init; }
        public DateTime OccurredUtc { get; init; }
        public Guid? CommentId { get; init; }
        public Guid? DocumentId { get; init; }
    }
}
