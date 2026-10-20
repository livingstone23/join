using JOIN.Application.Common;
using JOIN.Application.Services.Email;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Common;

/// <summary>
/// Maps the email template responses (SPEC 71) to HTTP: 401 without company, 404 for the
/// <c>*_NOT_FOUND</c> codes, 409 for <c>DUPLICATE_KEY</c> and <c>EMAIL_TEMPLATE_TYPE_IN_USE</c>,
/// 403 for <c>SUPERADMIN_REQUIRED</c>, 400 otherwise (for example
/// <c>EMAIL_TEMPLATE_UNKNOWN_VARIABLE</c>).
/// </summary>
public static class EmailTemplateResponseResults
{
    /// <summary>HTTP status of a failed response.</summary>
    public static int StatusCodeOf(string message) => message switch
    {
        EmailTemplateErrorCodes.CompanyRequired => StatusCodes.Status401Unauthorized,
        EmailTemplateErrorCodes.EmailTemplateNotFound
            or EmailTemplateErrorCodes.EmailTemplateTypeNotFound
            or EmailTemplateErrorCodes.PersonNotFound
            or EmailTemplateErrorCodes.LeadNotFound => StatusCodes.Status404NotFound,
        EmailTemplateErrorCodes.DuplicateKey or EmailTemplateErrorCodes.EmailTemplateTypeInUse => StatusCodes.Status409Conflict,
        EmailTemplateErrorCodes.SuperAdminRequired => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest
    };

    /// <summary>200 with the response, or the mapped error status.</summary>
    public static IActionResult ToEmailTemplateResult<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.Ok(response) : Fail(response);

    /// <summary>201 with the response, or the mapped error status.</summary>
    public static IActionResult ToEmailTemplateCreated<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.StatusCode(StatusCodes.Status201Created, response) : Fail(response);

    private static ObjectResult Fail<T>(Response<T> response) => new(response) { StatusCode = StatusCodeOf(response.Message) };
}
