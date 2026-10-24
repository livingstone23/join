using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UseCases.Catalog.Products.Documents.Commands;
using JOIN.Application.UseCases.Catalog.Products.Documents.Queries;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Catalog;

/// <summary>
/// SPEC 70 — documents of a product or service, classified by the company's <c>ProductDocumentTypes</c>, under the
/// <c>Products</c> resource. Upload and delete need <c>CanUpdate</c>, download <c>CanDownload</c>; restore is
/// SuperAdmin-only.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Products/{productId:guid}/documents")]
[Produces("application/json")]
[PermissionResource("Products")]
public class ProductDocumentsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyList<ProductDocumentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid productId, CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await mediator.Send(new GetProductDocumentsQuery(productId), cancellationToken));

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(200L * 1024 * 1024)] // techo; el límite real lo marca ProductAttachmentSettings
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<ProductDocumentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Upload(
        Guid productId,
        [FromForm] UploadProductDocumentForm form,
        CancellationToken cancellationToken = default)
    {
        // Mismo patrón que CalendarEventRecords: un DTO [FromForm] evita el 400 de
        // model binding cuando Refit manda las partes multipart con mayúsculas/minúsculas.
        if (form.File is null || form.File.Length == 0)
        {
            return this.ToCatalogResult(Response<ProductDocumentDto>.Error("INVALID_FILE", ["The 'File' form field is required."]));
        }

        if (form.ProductDocumentTypeId == Guid.Empty)
        {
            return this.ToCatalogResult(Response<ProductDocumentDto>.Error(
                CatalogErrorCodes.ProductDocumentTypeNotFound,
                ["ProductDocumentTypeId is required."]));
        }

        await using var buffer = new MemoryStream();
        await form.File.CopyToAsync(buffer, cancellationToken);
        var command = new UploadProductDocumentCommand(
            productId,
            form.ProductDocumentTypeId,
            // FE-45: the display name defaults to the file name.
            string.IsNullOrWhiteSpace(form.DisplayName) ? Path.GetFileNameWithoutExtension(form.File.FileName) : form.DisplayName,
            form.Description,
            form.File.FileName,
            form.File.ContentType ?? "application/octet-stream",
            buffer.ToArray());
        return this.ToCatalogCreated(await mediator.Send(command, cancellationToken));
    }

    [HttpGet("{documentId:guid}/download")]
    [RequirePermission(PermissionFlags.CanDownload)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Download(Guid productId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(new DownloadProductDocumentQuery(productId, documentId), cancellationToken);
        return response.IsSuccess
            ? File(response.Data!.Content, response.Data.ContentType, response.Data.OriginalName)
            : this.ToCatalogResult(response);
    }

    [HttpDelete("{documentId:guid}")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid productId, Guid documentId, CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await mediator.Send(new DeleteProductDocumentCommand(productId, documentId), cancellationToken));

    /// <summary>Restores a logically deleted document (SPEC 41-post). Restricted to the SuperAdmin role.</summary>
    [HttpPost("{documentId:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid productId, Guid documentId, CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await mediator.Send(new RestoreProductDocumentCommand(productId, documentId), cancellationToken));
}

/// <summary>Multipart upload body for product documents (SPEC 70 / FE-45).</summary>
public sealed class UploadProductDocumentForm
{
    public IFormFile? File { get; init; }
    public Guid ProductDocumentTypeId { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
}
