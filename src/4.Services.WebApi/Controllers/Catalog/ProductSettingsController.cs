using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.UseCases.Catalog.ProductAttachmentSettings;
using JOIN.Application.UseCases.Catalog.ProductDocumentTypes;
using JOIN.Services.WebApi.Controllers.Catalog;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Catalog;

/// <summary>
/// SPEC 70 — per-company product catalog settings. One controller for the new <c>ProductSettings</c> resource
/// (rule 5): the attachment limits (<c>ProductAttachmentSettings</c>) and the document type catalog
/// (<c>ProductDocumentTypes</c>). <c>POST</c>/<c>PUT</c>/<c>DELETE</c> on document types need <c>CanUpdate</c>;
/// <c>POST /restore</c> is reserved for SuperAdmin (mirrors <c>LeadSettings</c>).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("ProductSettings")]
public class ProductSettingsController(ISender sender) : ControllerBase
{
    // ---- ProductAttachmentSettings ----

    [HttpGet("attachment-settings")]
    [ProducesResponseType(typeof(Response<ProductAttachmentSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAttachmentSettings(CancellationToken cancellationToken) =>
        this.ToCatalogResult(await sender.Send(new GetProductAttachmentSettingsQuery(), cancellationToken));

    [HttpPost("attachment-settings")]
    [ProducesResponseType(typeof(Response<ProductAttachmentSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAttachmentSettings([FromBody] CreateProductAttachmentSettingsCommand command, CancellationToken cancellationToken) =>
        this.ToCatalogResult(await sender.Send(command, cancellationToken));

    [HttpPut("attachment-settings")]
    [ProducesResponseType(typeof(Response<ProductAttachmentSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAttachmentSettings([FromBody] UpdateProductAttachmentSettingsCommand command, CancellationToken cancellationToken) =>
        this.ToCatalogResult(await sender.Send(command, cancellationToken));

    // ---- ProductDocumentTypes ----

    [HttpGet("document-types")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<ProductDocumentTypeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDocumentTypes([FromQuery] bool? includeInactive = null, CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await sender.Send(new GetProductDocumentTypesQuery(includeInactive), cancellationToken));

    [HttpPost("document-types")]
    [ProducesResponseType(typeof(Response<ProductDocumentTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDocumentType([FromBody] CreateProductDocumentTypeCommand command, CancellationToken cancellationToken) =>
        this.ToCatalogResult(await sender.Send(command, cancellationToken));

    [HttpPut("document-types/{id:guid}")]
    [ProducesResponseType(typeof(Response<ProductDocumentTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateDocumentType(Guid id, [FromBody] UpdateProductDocumentTypeCommand command, CancellationToken cancellationToken)
    {
        var updated = command with { Id = id };
        return this.ToCatalogResult(await sender.Send(updated, cancellationToken));
    }

    [HttpDelete("document-types/{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDocumentType(Guid id, CancellationToken cancellationToken) =>
        this.ToCatalogResult(await sender.Send(new DeleteProductDocumentTypeCommand(id), cancellationToken));

    [HttpPost("document-types/{id:guid}/restore")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreDocumentType(Guid id, CancellationToken cancellationToken) =>
        this.ToCatalogResult(await sender.Send(new RestoreProductDocumentTypeCommand(id), cancellationToken));
}