using JOIN.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers;

/// <summary>
/// Maps the response of a <c>Restore&lt;Entity&gt;Command</c> (SPEC 41) to an HTTP result, so every
/// <c>POST /{id}/restore</c> endpoint answers the same status codes:
/// <c>404</c> not found (or another company's row), <c>409</c> not deleted / parent deleted /
/// active duplicate, <c>403</c> caller is not SuperAdmin, <c>400</c> anything else.
/// </summary>
public static class RestoreResponseExtensions
{
    public static IActionResult ToRestoreResult(this ControllerBase controller, Response<Guid> response)
    {
        if (response.IsSuccess)
        {
            return controller.Ok(response);
        }

        return response.Message switch
        {
            "NOT_FOUND" => controller.NotFound(response),
            "NOT_DELETED" or "PARENT_DELETED" or "ACTIVE_DUPLICATE_EXISTS" => controller.Conflict(response),
            "SUPERADMIN_REQUIRED" => controller.StatusCode(StatusCodes.Status403Forbidden, response),
            _ => controller.BadRequest(response)
        };
    }
}
