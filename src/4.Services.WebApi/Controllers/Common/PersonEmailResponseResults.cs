using JOIN.Application.Common;
using JOIN.Application.Services.Email;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Common;

/// <summary>
/// Maps the email responses (SPEC 72) to HTTP: 401 without company or user, 404 for the <c>*_NOT_FOUND</c> codes,
/// 409 for <c>PERSON_NOT_ACTIVE</c>, <c>LEAD_NOT_OPEN</c> and an existing settings row, 429 for the daily limit, 502 when
/// the provider rejected the email (the data carries the <c>Failed</c> row), 503 without the storage provider, 400 otherwise.
/// </summary>
public static class PersonEmailResponseResults
{
    /// <summary>HTTP status of a failed response.</summary>
    public static int StatusCodeOf(string message) => message switch
    {
        PersonEmailErrorCodes.CompanyRequired or PersonEmailErrorCodes.UserRequired => StatusCodes.Status401Unauthorized,
        PersonEmailErrorCodes.SuperAdminRequired => StatusCodes.Status403Forbidden,
        PersonEmailErrorCodes.PersonNotActive
            or PersonEmailErrorCodes.LeadNotOpen
            or PersonEmailErrorCodes.EmailSettingsAlreadyExists => StatusCodes.Status409Conflict,
        PersonEmailErrorCodes.EmailDailyLimitReached => StatusCodes.Status429TooManyRequests,
        PersonEmailErrorCodes.EmailSendFailed => StatusCodes.Status502BadGateway,
        PersonEmailErrorCodes.StorageProviderNotConfigured => StatusCodes.Status503ServiceUnavailable,
        PersonEmailErrorCodes.FileNotFoundInStorage => StatusCodes.Status404NotFound,
        _ when message.EndsWith("_NOT_FOUND", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest
    };

    /// <summary>200 with the response, or the mapped error status.</summary>
    public static IActionResult ToPersonEmailResult<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.Ok(response) : Fail(response);

    /// <summary>201 with the response, or the mapped error status.</summary>
    public static IActionResult ToPersonEmailCreated<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.StatusCode(StatusCodes.Status201Created, response) : Fail(response);

    private static ObjectResult Fail<T>(Response<T> response) => new(response) { StatusCode = StatusCodeOf(response.Message) };
}
