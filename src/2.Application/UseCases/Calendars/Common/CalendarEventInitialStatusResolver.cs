using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Calendars;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Resolves the status a new activity starts in (SPEC 44 R6): always
/// <see cref="CalendarActivityCompany.InitialCalendarStatusCompanyId"/>, whoever creates it. Same pattern as
/// <c>TicketInitialStatusResolver</c> (SPEC 42). Shared with channel intake (SPEC 45).
/// </summary>
public sealed class CalendarEventInitialStatusResolver(IUnitOfWork unitOfWork, CalendarCatalogReader catalogReader)
{
    /// <summary>
    /// Returns the initial status of an activity of the given type, or <c>CALENDAR_STATUS_NOT_ENABLED</c> when the
    /// configured status is not usable.
    /// </summary>
    public async Task<Response<CalendarStatusCompany>> ResolveAsync(CalendarActivityCompany activityCompany)
    {
        var status = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(activityCompany.InitialCalendarStatusCompanyId);
        var usable = await catalogReader.GetUsableStatusAsync(activityCompany.CompanyId, status);
        return usable is null
            ? Response<CalendarStatusCompany>.Error(CalendarErrorCodes.StatusNotEnabled,
                ["The initial status configured for this activity type is not active. Fix it in CalendarActivityCompanies."])
            : new Response<CalendarStatusCompany> { IsSuccess = true, Data = status };
    }
}
