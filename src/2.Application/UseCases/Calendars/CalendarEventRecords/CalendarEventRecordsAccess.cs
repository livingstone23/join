using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.CalendarActivityIndex;
using JOIN.Application.UseCases.Calendars.CalendarPanel;
using JOIN.Domain.Security;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// SPEC 69 rule 5 — who can see an activity's comments and documents: the owner, a user with the
/// <c>CanViewOthersActivities</c> flag (SPEC 68, via <see cref="CalendarActivityVisibility"/>), or a user with the
/// Panel's <c>CalendarOthers.CanRead</c>. A SuperAdmin passes, as in the global filter. Anything else resolves as
/// not found, so a hidden activity is indistinguishable from a missing one.
/// </summary>
public sealed class CalendarEventRecordsAccess(
    CalendarActivityVisibility visibility,
    IPermissionService permissionService,
    ICurrentUserService currentUserService)
{
    public async Task<CalendarActivityAccessResult> ResolveAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var access = await visibility.ResolveEventAccessAsync(eventId, cancellationToken);
        if (access.IsFoundAndVisible || !access.Found || access.CalendarEvent is null)
        {
            return access;
        }

        return await CanReadOthersAsync()
            ? CalendarActivityAccessResult.Visible(access.CalendarEvent, access.CurrentUserId)
            : CalendarActivityAccessResult.NotFound();
    }

    private async Task<bool> CanReadOthersAsync()
    {
        if (currentUserService.IsInRole("SuperAdmin"))
        {
            return true;
        }

        return await permissionService.HasPermissionAsync(
            currentUserService.UserId ?? string.Empty,
            currentUserService.CompanyId.ToString(),
            CalendarPanelAccess.OthersResource,
            "GET",
            PermissionFlags.CanRead);
    }
}
