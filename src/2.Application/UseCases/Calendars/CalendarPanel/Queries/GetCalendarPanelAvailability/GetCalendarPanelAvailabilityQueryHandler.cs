using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelAvailability;

/// <summary>
/// Handles <see cref="GetCalendarPanelAvailabilityQuery"/>: Dapper reads plus <see cref="CalendarAvailabilityCalculator"/>.
/// </summary>
public sealed class GetCalendarPanelAvailabilityQueryHandler(
    ISqlConnectionFactory connectionFactory,
    CalendarQueryReader reader,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarPanelAvailabilityQuery, Response<CalendarAvailabilityDto>>
{
    /// <summary> Maximum range of an availability query. </summary>
    public const int MaxDays = 14;

    private const string UserSql = """
        SELECT c.Id AS ConfigurationId, COALESCE(c.WorkDays, 0) AS WorkDays, c.BusinessHoursStart, c.BusinessHoursEnd
        FROM Security.UserCompanies uc
        LEFT JOIN Calendar.UserConfigurations c ON c.UserId = uc.UserId AND c.CompanyId = @TenantId AND c.GcRecord = 0
        WHERE uc.CompanyId = @TenantId AND uc.UserId = @UserId AND uc.GcRecord = 0
        LIMIT 1;
        """;

    private const string ActivitySql = """
        SELECT ac.DefaultDurationMinutes, ac.AllowsOverlap, ac.RequiresBusinessHours
        FROM Calendar.ActivityCompanies ac
        INNER JOIN Calendar.Activities a ON a.Id = ac.CalendarActivityId
        WHERE ac.CompanyId = @TenantId AND ac.Id = @Id
          AND ac.GcRecord = 0 AND ac.IsActive = TRUE
          AND a.GcRecord = 0 AND a.IsActive = TRUE;
        """;

    public async Task<Response<CalendarAvailabilityDto>> Handle(GetCalendarPanelAvailabilityQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarAvailabilityDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var fromUtc = request.From.UtcDateTime;
        var toUtc = request.To.UtcDateTime;
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(MaxDays))
        {
            return Response<CalendarAvailabilityDto>.Error(CalendarErrorCodes.RangeTooLarge, [$"'to' must be later than 'from' and the range cannot exceed {MaxDays} days."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var company = await reader.GetCalendarCompanyAsync(connection, companyId, cancellationToken);
        if (company is null)
        {
            return Response<CalendarAvailabilityDto>.Error(CalendarErrorCodes.CompanyNotConfigured, ["The company has no calendar configuration."]);
        }

        var user = await connection.QueryFirstOrDefaultAsync<UserRow>(
            new CommandDefinition(UserSql, new { TenantId = companyId, request.UserId }, cancellationToken: cancellationToken));
        if (user is null)
        {
            return Response<CalendarAvailabilityDto>.Error(CalendarErrorCodes.UserNotInCompany, ["The user is not an active user of the current company."]);
        }

        if (user.ConfigurationId is null)
        {
            return Response<CalendarAvailabilityDto>.Error(CalendarErrorCodes.UserHasNoCalendar, ["The user has no calendar in this company yet."]);
        }

        var activity = await connection.QueryFirstOrDefaultAsync<ActivityRow>(
            new CommandDefinition(ActivitySql, new { TenantId = companyId, Id = request.ActivityCompanyId }, cancellationToken: cancellationToken));
        if (activity is null)
        {
            return Response<CalendarAvailabilityDto>.Error(CalendarErrorCodes.ActivityNotEnabled, ["The activity type is not enabled in the company."]);
        }

        var zone = CalendarTime.FindTimeZone(company.TimeZone) ?? TimeZoneInfo.Utc;
        var fromDate = CalendarTime.LocalDate(fromUtc, zone);
        var toDate = CalendarTime.LocalDate(toUtc.AddTicks(-1), zone);

        var occurrences = await reader.GetOccurrencesAsync(connection, companyId, [request.UserId], fromUtc, toUtc, company.TimeZone,
            new CalendarOccurrenceFilter(IncludeCancelled: true), cancellationToken);
        var periods = await reader.GetPeriodsAsync(connection, companyId, [request.UserId], fromDate, toDate, cancellationToken);
        var holidays = await reader.GetHolidaysAsync(connection, companyId, fromDate, toDate, cancellationToken);

        var (start, end) = (user.BusinessHoursStart, user.BusinessHoursEnd) is ({ } ownStart, { } ownEnd)
            ? (ownStart, (TimeOnly?)ownEnd)
            : company.IsOpen24Hours || company.BusinessHoursStart is null
                ? (TimeOnly.MinValue, null)
                : (company.BusinessHoursStart.Value, company.BusinessHoursEnd);

        var slots = CalendarAvailabilityCalculator.Compute(
            fromUtc,
            toUtc,
            DateTime.UtcNow,
            zone,
            activity.DefaultDurationMinutes,
            activity.RequiresBusinessHours,
            activity.AllowsOverlap,
            (CalendarWorkDays)user.WorkDays,
            new CalendarBusinessHours(start, end),
            periods.Select(p => (p.StartDate, p.EndDate)).ToList(),
            holidays.Select(h => h.OccursOn).ToList(),
            occurrences.Where(o => !o.Activity.AllowsOverlap && o.Status.BlocksTime).Select(o => (o.StartUtc, o.EndUtc)).ToList());

        return new Response<CalendarAvailabilityDto>
        {
            IsSuccess = true,
            Message = "Availability retrieved successfully.",
            Data = new CalendarAvailabilityDto
            {
                UserId = request.UserId,
                ActivityCompanyId = request.ActivityCompanyId,
                DurationMinutes = activity.DefaultDurationMinutes,
                TimeZone = company.TimeZone,
                Slots = slots
            }
        };
    }

    private sealed record UserRow
    {
        public Guid? ConfigurationId { get; init; }
        public int WorkDays { get; init; }
        public TimeOnly? BusinessHoursStart { get; init; }
        public TimeOnly? BusinessHoursEnd { get; init; }
    }

    private sealed record ActivityRow
    {
        public int DefaultDurationMinutes { get; init; }
        public bool AllowsOverlap { get; init; }
        public bool RequiresBusinessHours { get; init; }
    }
}
