using JOIN.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Customers;

/// <summary>
/// Maps the commercial module responses (SPEC 59–61) to HTTP: 401 without company or user, 403 for
/// <c>SUPERADMIN_REQUIRED</c> and <c>LEAD_ACTION_FORBIDDEN</c>, 404 for <c>*_NOT_FOUND</c>, 409 for <c>*_IN_USE</c>,
/// <c>DUPLICATE_KEY</c> and the lead state conflicts, 400 otherwise.
/// </summary>
public static class CommercialResponseResults
{
    private static readonly HashSet<string> Conflicts =
    [
        "DUPLICATE_KEY", "LEAD_DUPLICATE_OPEN", "LEAD_NOT_OPEN", "LEAD_NOT_INACTIVE", "LEAD_CURRENCY_LOCKED", "LEAD_PERSON_INACTIVE"
    ];

    /// <summary>HTTP status of a failed response.</summary>
    public static int StatusCodeOf(string message) => message switch
    {
        "COMPANY_REQUIRED" or "USER_REQUIRED" => StatusCodes.Status401Unauthorized,
        "SUPERADMIN_REQUIRED" or "LEAD_ACTION_FORBIDDEN" => StatusCodes.Status403Forbidden,
        _ when Conflicts.Contains(message) => StatusCodes.Status409Conflict,
        // A missing rate for the lead's currency is a request problem (SPEC 61 errors table), not a missing resource.
        "EXCHANGE_RATE_NOT_FOUND" => StatusCodes.Status400BadRequest,
        // SPEC 49-post: the row asks for a provider this environment no longer has (recoverable by ops).
        "STORAGE_PROVIDER_NOT_CONFIGURED" => StatusCodes.Status503ServiceUnavailable,
        _ when message.EndsWith("_NOT_FOUND", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        _ when message.EndsWith("_IN_USE", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };

    /// <summary>200 with the response, or the mapped error status.</summary>
    public static IActionResult ToCommercialResult<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.Ok(response) : Fail(response);

    /// <summary>201 with the response, or the mapped error status.</summary>
    public static IActionResult ToCommercialCreated<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.StatusCode(StatusCodes.Status201Created, response) : Fail(response);

    private static ObjectResult Fail<T>(Response<T> response) => new(response) { StatusCode = StatusCodeOf(response.Message) };
}
