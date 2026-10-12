using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies;

/// <summary>
/// Rules to deactivate or delete a company status (SPEC 44, data model 4): not the initial status of a type
/// (<c>CALENDAR_STATUS_IN_USE_AS_INITIAL</c>), not the reschedule status of the company
/// (<c>CALENDAR_STATUS_IN_USE_AS_RESCHEDULE</c>), without activities (<c>CALENDAR_STATUS_IN_USE</c>), and never
/// <c>PENDING</c> or <c>CANCELLED</c> (<c>CALENDAR_STATUS_REQUIRED_BY_SYSTEM</c>).
/// </summary>
public sealed class CalendarStatusCompanyRemovalGuard(IUnitOfWork unitOfWork)
{
    /// <summary> Company statuses the logic needs. </summary>
    public static readonly string[] RequiredCodes = [CalendarCodes.Statuses.Pending, CalendarCodes.Statuses.Cancelled];

    public async Task<Response<bool>> CanRemoveAsync(CalendarStatusCompany status, CalendarStatus? global, CancellationToken cancellationToken)
    {
        if (global is not null && RequiredCodes.Contains(global.Code))
        {
            return Response<bool>.Error(CalendarErrorCodes.StatusRequiredBySystem, [$"{global.Code} is required by the calendar logic."]);
        }

        var isInitial = (await unitOfWork.GetRepository<CalendarActivityCompany>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == status.CompanyId && x.InitialCalendarStatusCompanyId == status.Id && x.GcRecord == 0))
            .Any();
        if (isInitial)
        {
            return Response<bool>.Error(CalendarErrorCodes.StatusInUseAsInitial, ["The status is the initial status of an activity type."]);
        }

        var isReschedule = (await unitOfWork.GetRepository<CalendarCompany>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == status.CompanyId && x.RescheduledByOthersStatusCompanyId == status.Id && x.GcRecord == 0))
            .Any();
        if (isReschedule)
        {
            return Response<bool>.Error(CalendarErrorCodes.StatusInUseAsReschedule, ["The status is the reschedule status of the company."]);
        }

        var hasEvents = await unitOfWork.CalendarEvents.AnyAsync(
            e => e.CompanyId == status.CompanyId && e.CalendarStatusCompanyId == status.Id && e.GcRecord == 0, cancellationToken);
        return hasEvents
            ? Response<bool>.Error(CalendarErrorCodes.StatusInUse, ["The status has activities."])
            : new Response<bool> { IsSuccess = true, Data = true };
    }
}
