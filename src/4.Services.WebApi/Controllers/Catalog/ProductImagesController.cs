using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.UseCases.Catalog.Products.Images.Commands;
using JOIN.Application.UseCases.Catalog.Products.Images.Queries;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Catalog;

/// <summary>
/// SPEC 70 — images of a product or service (0 to 5, one primary), under the <c>Products</c> resource. Every
/// write needs <c>CanUpdate</c> on the product; <c>POST /restore</c> is SuperAdmin-only.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Products/{productId:guid}/images")]
[Produces("application/json")]
[PermissionResource("Products")]
public class ProductImagesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyList<ProductImageDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid productId, CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await mediator.Send(new GetProductImagesQuery(productId), cancellationToken));

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(200L * 1024 * 1024)] // techo; el límite real lo marca ProductAttachmentSettings
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<ProductImageDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Upload(Guid productId, IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return this.ToCatalogResult(Response<ProductImageDto>.Error("INVALID_FILE", ["The 'file' form field is required."]));
        }

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var command = new UploadProductImageCommand(productId, file.FileName, file.ContentType ?? "application/octet-stream", buffer.ToArray());
        return this.ToCatalogCreated(await mediator.Send(command, cancellationToken));
    }

    [HttpPut("order")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reorder(Guid productId, [FromBody] ReorderProductImagesCommand command, CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await mediator.Send(command with { ProductId = productId }, cancellationToken));

    [HttpPut("{imageId:guid}/primary")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPrimary(Guid productId, Guid imageId, CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await mediator.Send(new SetPrimaryProductImageCommand(productId, imageId), cancellationToken));

    [HttpGet("{imageId:guid}/content")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Content(Guid productId, Guid imageId, CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(new GetProductImageContentQuery(productId, imageId), cancellationToken);
        return response.IsSuccess ? this.ToInlineImage(response.Data!) : this.ToCatalogResult(response);
    }

    [HttpDelete("{imageId:guid}")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid productId, Guid imageId, CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await mediator.Send(new DeleteProductImageCommand(productId, imageId), cancellationToken));

    /// <summary>Restores a logically deleted image (SPEC 41-post). Restricted to the SuperAdmin role.</summary>
    [HttpPost("{imageId:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid productId, Guid imageId, CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await mediator.Send(new RestoreProductImageCommand(productId, imageId), cancellationToken));
}
