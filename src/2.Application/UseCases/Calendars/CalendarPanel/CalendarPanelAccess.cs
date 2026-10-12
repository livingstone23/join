using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Security;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel;

/// <summary>
/// Second permission of the panel status change (SPEC 44, "Permisos"): the global filter resolves
/// <c>CalendarActivityConfirmation</c> for the action, and this check adds <c>CalendarOthers.CanUpdate</c> with the
/// same <see cref="IPermissionService"/>. A SuperAdmin passes, as in the filter.
/// </summary>
public sealed class CalendarPanelAccess(IPermissionService permissionService, ICurrentUserService currentUserService)
{
    /// <summary> Permission resource of the panel. </summary>
    public const string OthersResource = "CalendarOthers";

    public async Task<Response<bool>> RequireOthersUpdateAsync()
    {
        if (currentUserService.IsInRole("SuperAdmin"))
        {
            return new Response<bool> { IsSuccess = true, Data = true };
        }

        var allowed = await permissionService.HasPermissionAsync(
            currentUserService.UserId ?? string.Empty,
            currentUserService.CompanyId.ToString(),
            OthersResource,
            "PATCH",
            PermissionFlags.CanUpdate);

        return allowed
            ? new Response<bool> { IsSuccess = true, Data = true }
            : Response<bool>.Error(CalendarErrorCodes.OthersPermissionRequired, ["Changing the status of another user's activity also requires CalendarOthers.CanUpdate."]);
    }
}
