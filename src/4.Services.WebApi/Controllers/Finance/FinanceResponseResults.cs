using JOIN.Application.Common;
using JOIN.Application.Services.Finance;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Finance;

/// <summary>
/// Maps the finance responses (SPEC 56) to HTTP: 401 without company or user, 404 for missing records, 409 for
/// <c>CURRENCY_IN_USE</c>, <c>BASE_CURRENCY_IN_USE</c> and <c>DUPLICATE_KEY</c>, 403 for <c>SUPERADMIN_REQUIRED</c>,
/// 400 otherwise (including <c>EXCHANGE_RATE_NOT_FOUND</c>, which is a validation error, not a missing record).
/// </summary>
public static class FinanceResponseResults
{
    /// <summary>HTTP status of a failed response.</summary>
    public static int StatusCodeOf(string message) => message switch
    {
        FinanceErrorCodes.CompanyRequired or FinanceErrorCodes.UserRequired => StatusCodes.Status401Unauthorized,
        FinanceErrorCodes.CurrencyNotFound or FinanceErrorCodes.CompanyCurrencyNotFound or FinanceErrorCodes.ExchangeRateRecordNotFound => StatusCodes.Status404NotFound,
        FinanceErrorCodes.CurrencyInUse or FinanceErrorCodes.BaseCurrencyInUse or FinanceErrorCodes.DuplicateKey => StatusCodes.Status409Conflict,
        FinanceErrorCodes.SuperAdminRequired => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest
    };

    /// <summary>200 with the response, or the mapped error status.</summary>
    public static IActionResult ToFinanceResult<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.Ok(response) : Fail(response);

    /// <summary>201 with the response, or the mapped error status.</summary>
    public static IActionResult ToFinanceCreated<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.StatusCode(StatusCodes.Status201Created, response) : Fail(response);

    private static ObjectResult Fail<T>(Response<T> response) => new(response) { StatusCode = StatusCodeOf(response.Message) };
}
