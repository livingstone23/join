using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Calendars;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Requires the company's <see cref="CalendarCompany"/> and returns its context (SPEC 44 R1, step 2). It does not
/// check <c>CompanyModule</c>: blocking by module is the global authorization filter's job (SPEC 48).
/// </summary>
public sealed class CalendarCompanyGuard(IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Loads the calendar configuration of the company, or <c>CALENDAR_COMPANY_NOT_CONFIGURED</c>.
    /// </summary>
    public async Task<Response<CalendarCompanyContext>> GetContextAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var calendarCompany = (await unitOfWork.GetRepository<CalendarCompany>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0))
            .FirstOrDefault();

        if (calendarCompany is null)
        {
            return Response<CalendarCompanyContext>.Error(CalendarErrorCodes.CompanyNotConfigured,
                ["The company has no calendar configuration. Create it in CalendarCompanies first."]);
        }

        return new Response<CalendarCompanyContext>
        {
            IsSuccess = true,
            Data = CalendarCompanyContext.From(calendarCompany)
        };
    }
}

/// <summary>
/// Calendar configuration of a company, ready for the module's rules.
/// </summary>
public sealed record CalendarCompanyContext(
    Guid CompanyId,
    Guid CalendarCompanyId,
    string TimeZoneId,
    TimeZoneInfo TimeZone,
    bool IsOpen24Hours,
    TimeOnly? BusinessHoursStart,
    TimeOnly? BusinessHoursEnd,
    Guid DefaultActivityUserId,
    string DefaultCalendarName,
    Guid RescheduledByOthersStatusCompanyId)
{
    /// <summary>
    /// Builds the context; an unknown zone falls back to UTC (the zone is validated when it is saved).
    /// </summary>
    public static CalendarCompanyContext From(CalendarCompany entity) => new(
        entity.CompanyId,
        entity.Id,
        entity.TimeZone,
        CalendarTime.FindTimeZone(entity.TimeZone) ?? TimeZoneInfo.Utc,
        entity.IsOpen24Hours,
        entity.BusinessHoursStart,
        entity.BusinessHoursEnd,
        entity.DefaultActivityUserId,
        entity.DefaultCalendarName,
        entity.RescheduledByOthersStatusCompanyId);
}
