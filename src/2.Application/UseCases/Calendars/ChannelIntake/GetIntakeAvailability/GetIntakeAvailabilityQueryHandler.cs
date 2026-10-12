using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.GetIntakeAvailability;

/// <summary>
/// Reuses <see cref="CalendarAvailabilityCalculator"/> for R4/R5 (SPEC 44). The agent is never the owner, so the
/// business-hours rule always applies. <see cref="GetIntakeAvailabilityQuery.UserId"/> is optional: when null,
/// the company default user (<see cref="Domain.Calendars.CalendarCompany.DefaultActivityUserId"/>) is used.
/// </summary>
public sealed class GetIntakeAvailabilityQueryHandler(
    ISqlConnectionFactory connectionFactory,
    CalendarQueryReader reader,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetIntakeAvailabilityQuery, Response<IntakeAvailabilityDto>>
{
    /// <summary> Maximum range of an intake availability query (SPEC 45). </summary>
    public const int MaxDays = 14;

    private const string UserSql = """
        SELECT c.Id AS ConfigurationId, COALESCE(c.WorkDays, 0) AS WorkDays, c.BusinessHoursStart, c.BusinessHoursEnd
        FROM Security.UserCompanies uc
        LEFT JOIN Calendar.UserConfigurations c ON c.UserId = uc.UserId AND c.CompanyId = @TenantId AND c.GcRecord = 0
        WHERE uc.CompanyId = @TenantId AND uc.UserId = @UserId AND uc.GcRecord = 0
        LIMIT 1;
        """;

    private const string CalendarCompanyDefaultUserSql = """
        SELECT cc.DefaultActivityUserId
          FROM Calendar.CalendarCompanies cc
         WHERE cc.CompanyId = @TenantId AND cc.GcRecord = 0
         LIMIT 1;
        """;

    private const string ActivityByCodeSql = """
        SELECT ac.Id AS ActivityCompanyId, ac.DefaultDurationMinutes, ac.AllowsOverlap, ac.RequiresBusinessHours
        FROM Calendar.ActivityCompanies ac
        INNER JOIN Calendar.Activities a ON a.Id = ac.CalendarActivityId
        WHERE ac.CompanyId = @TenantId
          AND ac.GcRecord = 0 AND ac.IsActive = TRUE
          AND a.GcRecord = 0 AND a.IsActive = TRUE
          AND a.Code = @Code
        LIMIT 1;
        """;

    public async Task<Response<IntakeAvailabilityDto>> Handle(GetIntakeAvailabilityQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<IntakeAvailabilityDto>.Error(CalendarErrorCodes.CompanyRequired,
                ["The X-Company-Id header or the CompanyId claim is required."]);
        }

        var fromUtc = request.From.UtcDateTime;
        var toUtc = request.To.UtcDateTime;
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(MaxDays))
        {
            return Response<IntakeAvailabilityDto>.Error(CalendarErrorCodes.RangeTooLarge,
                [$"'to' must be later than 'from' and the range cannot exceed {MaxDays} days."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var company = await reader.GetCalendarCompanyAsync(connection, companyId, cancellationToken);
        if (company is null)
        {
            return Response<IntakeAvailabilityDto>.Error(CalendarErrorCodes.CompanyNotConfigured,
                ["The company has no calendar configuration."]);
        }

        Guid resolvedUserId;
        if (request.UserId.HasValue && request.UserId.Value != Guid.Empty)
        {
            resolvedUserId = request.UserId.Value;
        }
        else
        {
            var defaultUserId = await connection.QueryFirstOrDefaultAsync<Guid?>(
                new CommandDefinition(CalendarCompanyDefaultUserSql, new { TenantId = companyId }, cancellationToken: cancellationToken));
            if (defaultUserId is null)
            {
                return Response<IntakeAvailabilityDto>.Error(CalendarErrorCodes.CompanyNotConfigured,
                    ["The company has no calendar configuration."]);
            }
            resolvedUserId = defaultUserId.Value;
        }

        var user = await connection.QueryFirstOrDefaultAsync<UserRow>(
            new CommandDefinition(UserSql, new { TenantId = companyId, UserId = resolvedUserId }, cancellationToken: cancellationToken));
        if (user is null)
        {
            return Response<IntakeAvailabilityDto>.Error(CalendarErrorCodes.UserNotInCompany,
                ["The user is not an active user of the current company."]);
        }

        if (user.ConfigurationId is null)
        {
            return Response<IntakeAvailabilityDto>.Error(CalendarErrorCodes.IntakeTargetUserHasNoCalendar,
                ["The target user has no calendar in this company yet."]);
        }

        var activity = await connection.QueryFirstOrDefaultAsync<ActivityRow>(
            new CommandDefinition(ActivityByCodeSql, new { TenantId = companyId, Code = request.ActivityCode.Trim().ToUpperInvariant() }, cancellationToken: cancellationToken));
        if (activity is null)
        {
            return Response<IntakeAvailabilityDto>.Error(CalendarErrorCodes.ActivityNotEnabled,
                ["The activity type is not enabled in the company."]);
        }

        var zone = CalendarTime.FindTimeZone(company.TimeZone) ?? TimeZoneInfo.Utc;
        var fromDate = CalendarTime.LocalDate(fromUtc, zone);
        var toDate = CalendarTime.LocalDate(toUtc.AddTicks(-1), zone);

        var occurrences = await reader.GetOccurrencesAsync(connection, companyId, [resolvedUserId], fromUtc, toUtc, company.TimeZone,
            new CalendarOccurrenceFilter(IncludeCancelled: true), cancellationToken);
        var periods = await reader.GetPeriodsAsync(connection, companyId, [resolvedUserId], fromDate, toDate, cancellationToken);
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

        return new Response<IntakeAvailabilityDto>
        {
            IsSuccess = true,
            Data = new IntakeAvailabilityDto(
                company.TimeZone,
                slots.Select(s => new IntakeAvailabilitySlotDto(s.StartUtc, s.EndUtc)).ToList()),
            Message = "Intake availability retrieved successfully."
        };
    }

    private sealed class UserRow
    {
        public Guid? ConfigurationId { get; init; }
        public int WorkDays { get; init; }
        public TimeOnly? BusinessHoursStart { get; init; }
        public TimeOnly? BusinessHoursEnd { get; init; }
    }

    private sealed class ActivityRow
    {
        public Guid ActivityCompanyId { get; init; }
        public int DefaultDurationMinutes { get; init; }
        public bool AllowsOverlap { get; init; }
        public bool RequiresBusinessHours { get; init; }
    }
}