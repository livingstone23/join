using JOIN.Application.Common;
using JOIN.Application.UseCases.Calendars.Common;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Maps the calendar responses (SPEC 44) to HTTP: 404 for <c>*_NOT_FOUND</c> (also another user's resource, R2),
/// 409 for conflicts (<c>CALENDAR_EVENT_OVERLAP</c>, duplicates, resources in use), 422 for the availability rules
/// (R5), 403 for the missing <c>CalendarOthers.CanUpdate</c> and 400 otherwise.
/// </summary>
public static class CalendarResponseResults
{
    private static readonly HashSet<string> Conflicts =
    [
        CalendarErrorCodes.EventOverlap,
        CalendarErrorCodes.ActivityCodeDuplicate,
        CalendarErrorCodes.StatusCodeDuplicate,
        CalendarErrorCodes.ActivityAlreadyEnabled,
        CalendarErrorCodes.StatusAlreadyEnabled,
        CalendarErrorCodes.CompanyAlreadyConfigured,
        CalendarErrorCodes.ActivityInUse,
        CalendarErrorCodes.ActivityRequiredBySystem,
        CalendarErrorCodes.StatusInUse,
        CalendarErrorCodes.StatusInUseAsInitial,
        CalendarErrorCodes.StatusInUseAsReschedule,
        CalendarErrorCodes.StatusRequiredBySystem,
        CalendarErrorCodes.NonWorkingDayDuplicate,
        CalendarErrorCodes.NonWorkingPeriodOverlap,
        CalendarErrorCodes.CalendarNameDuplicate,
        CalendarErrorCodes.CalendarIsDefault,
        CalendarErrorCodes.CalendarHasActiveEvents,
        CalendarErrorCodes.UserHoursOutOfCompanyRange,
        CalendarErrorCodes.IntakeContactBelongsToOtherPerson
    ];

    private static readonly HashSet<string> Unprocessable =
    [
        CalendarErrorCodes.OutsideWorkDays,
        CalendarErrorCodes.UserNotAvailable,
        CalendarErrorCodes.CompanyHoliday,
        CalendarErrorCodes.OutsideBusinessHours
    ];

    /// <summary>
    /// HTTP status of a failed response.
    /// </summary>
    public static int StatusCodeOf(string message) => message switch
    {
        _ when message.EndsWith("_NOT_FOUND", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        _ when Conflicts.Contains(message) => StatusCodes.Status409Conflict,
        _ when Unprocessable.Contains(message) => StatusCodes.Status422UnprocessableEntity,
        CalendarErrorCodes.OthersPermissionRequired or CalendarErrorCodes.IntakeCompanyNotAllowed => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest
    };

    /// <summary>
    /// 200 with the response, or the mapped error status.
    /// </summary>
    public static IActionResult ToResult<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.Ok(response) : Fail(response);

    /// <summary>
    /// 201 with the response, or the mapped error status.
    /// </summary>
    public static IActionResult ToCreated<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.StatusCode(StatusCodes.Status201Created, response) : Fail(response);

    private static ObjectResult Fail<T>(Response<T> response) => new(response) { StatusCode = StatusCodeOf(response.Message) };
}
