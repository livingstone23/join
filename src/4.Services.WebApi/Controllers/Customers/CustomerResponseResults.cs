using JOIN.Application.Common;
using JOIN.Application.Services.Customers;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Customers;

/// <summary>
/// Maps the customer module responses (SPEC 55) to HTTP: 401 without company or user, 404 for <c>*_NOT_FOUND</c>,
/// 409 for <c>CUSTOMER_STAGE_IN_USE</c> and <c>DUPLICATE_KEY</c>, 400 otherwise.
/// </summary>
public static class CustomerResponseResults
{
    /// <summary>
    /// HTTP status of a failed response.
    /// </summary>
    public static int StatusCodeOf(string message) => message switch
    {
        CustomerErrorCodes.CompanyRequired or CustomerErrorCodes.UserRequired => StatusCodes.Status401Unauthorized,
        _ when message.EndsWith("_NOT_FOUND", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        CustomerErrorCodes.CustomerStageInUse or CustomerErrorCodes.DuplicateKey => StatusCodes.Status409Conflict,
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
