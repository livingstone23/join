using JOIN.Application.Common;
using JOIN.Application.Services.Customers;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Customers.LeadSettings;

/// <summary>
/// Maps the commercial settings responses (SPEC 59) to HTTP: 401 without company, 404 for <c>*_NOT_FOUND</c>, 409 for
/// <c>*_IN_USE</c> and <c>DUPLICATE_KEY</c>, 403 for <c>SUPERADMIN_REQUIRED</c>, 400 otherwise.
/// </summary>
public static class LeadSettingsResponseResults
{
    /// <summary>HTTP status of a failed response.</summary>
    public static int StatusCodeOf(string message) => message switch
    {
        LeadSettingsErrorCodes.CompanyRequired => StatusCodes.Status401Unauthorized,
        LeadSettingsErrorCodes.SuperAdminRequired => StatusCodes.Status403Forbidden,
        LeadSettingsErrorCodes.DuplicateKey => StatusCodes.Status409Conflict,
        _ when message.EndsWith("_NOT_FOUND", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        _ when message.EndsWith("_IN_USE", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };

    /// <summary>200 with the response, or the mapped error status.</summary>
    public static IActionResult ToLeadSettingsResult<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.Ok(response) : Fail(response);

    /// <summary>201 with the response, or the mapped error status.</summary>
    public static IActionResult ToLeadSettingsCreated<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.StatusCode(StatusCodes.Status201Created, response) : Fail(response);

    private static ObjectResult Fail<T>(Response<T> response) => new(response) { StatusCode = StatusCodeOf(response.Message) };
}
