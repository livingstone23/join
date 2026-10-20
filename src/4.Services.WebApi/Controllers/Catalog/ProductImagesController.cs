using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.UseCases.Catalog.ProductImages;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Catalog;

/// <summary>
/// SPEC 70 — images of a product. <c>GET /content</c> streams the bytes with a private cache header
/// (rule 7 in the spec); <c>POST /restore</c> is SuperAdmin-only.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Products/{productId:guid}/images")]
[Produces("application/json")]
[PermissionResource("Products")]
public class ProductImagesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyList<ProductImageDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid productId, CancellationToken cancellationToken) =>
        this.ToCatalogResult(await sender.Send(new GetProductImagesQuery(productId), cancellationToken));

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(Response<ProductImageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Upload(Guid productId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return this.ToCatalogResult(Response<ProductImageDto>.Error("INVALID_FILE", ["The 'file' form field is required."]));
        }

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        var command = new UploadProductImageCommand(
            productId,
            file.FileName,
            file.ContentType ?? "application/octet-stream",
            ms.ToArray());
        return this.ToCatalogResult(await sender.Send(command, cancellationToken));
    }

    [HttpPut("order")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reorder(Guid productId, [FromBody] ReorderProductImagesCommand body, CancellationToken cancellationToken)
    {
        var cmd = body with { ProductId = productId };
        return this.ToCatalogResult(await sender.Send(cmd, cancellationToken));
    }

    [HttpPut("{imageId:guid}/primary")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPrimary(Guid productId, Guid imageId, CancellationToken cancellationToken) =>
        this.ToCatalogResult(await sender.Send(new SetPrimaryProductImageCommand(productId, imageId), cancellationToken));

    [HttpGet("{imageId:guid}/content")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Content(Guid productId, Guid imageId, CancellationToken cancellationToken)
    {
        var response = await sender.Send(new GetProductImageContentQuery(productId, imageId), cancellationToken);
        if (!response.IsSuccess)
        {
            return this.ToCatalogResult(response);
        }

        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(response.Data!.Content, response.Data.ContentType, response.Data.FileName);
    }

    [HttpDelete("{imageId:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid productId, Guid imageId, CancellationToken cancellationToken) =>
        this.ToCatalogResult(await sender.Send(new DeleteProductImageCommand(productId, imageId), cancellationToken));

    [HttpPost("{imageId:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(Guid productId, Guid imageId, CancellationToken cancellationToken) =>
        this.ToCatalogResult(await sender.Send(new RestoreProductImageCommand(productId, imageId), cancellationToken));
}