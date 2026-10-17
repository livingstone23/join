using JOIN.Application.Common;
using JOIN.Application.Services.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Catalog;

/// <summary>
/// Maps the catalog responses (SPEC 57) to HTTP: 401 without company, 404 for <c>*_NOT_FOUND</c>, 409 for
/// <c>*_IN_USE</c> and <c>DUPLICATE_KEY</c>, 403 for <c>SUPERADMIN_REQUIRED</c>, 400 otherwise.
/// </summary>
public static class CatalogResponseResults
{
    /// <summary>HTTP status of a failed response.</summary>
    public static int StatusCodeOf(string message) => message switch
    {
        CatalogErrorCodes.CompanyRequired => StatusCodes.Status401Unauthorized,
        CatalogErrorCodes.ProductNotFound or CatalogErrorCodes.ProductCategoryNotFound => StatusCodes.Status404NotFound,
        CatalogErrorCodes.ProductInUse or CatalogErrorCodes.ProductCategoryInUse or CatalogErrorCodes.DuplicateKey => StatusCodes.Status409Conflict,
        CatalogErrorCodes.SuperAdminRequired => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest
    };

    /// <summary>200 with the response, or the mapped error status.</summary>
    public static IActionResult ToCatalogResult<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.Ok(response) : Fail(response);

    /// <summary>201 with the response, or the mapped error status.</summary>
    public static IActionResult ToCatalogCreated<T>(this ControllerBase controller, Response<T> response) =>
        response.IsSuccess ? controller.StatusCode(StatusCodes.Status201Created, response) : Fail(response);

    private static ObjectResult Fail<T>(Response<T> response) => new(response) { StatusCode = StatusCodeOf(response.Message) };
}
