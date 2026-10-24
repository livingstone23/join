using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.UseCases.Catalog.ProductDocumentTypes.Commands;
using JOIN.Application.UseCases.Catalog.ProductDocumentTypes.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Catalog;

/// <summary>
/// The company's catalog of product document types (SPEC 70), "Tipos de documento" tab of the product settings.
/// Resource <c>ProductSettings</c>; restore is reserved for SuperAdmin (SPEC 41-post).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("ProductSettings")]
public class ProductDocumentTypesController(IMediator mediator) : ControllerBase
{
    /// <summary>The company's catalog of document types. <c>includeDeleted</c> only reaches a SuperAdmin (SPEC 72.1).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyList<ProductDocumentTypeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool? includeInactive = null,
        [FromQuery] bool? includeDeleted = null,
        CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await mediator.Send(new GetProductDocumentTypesQuery(includeInactive, includeDeleted), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(Response<ProductDocumentTypeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateProductDocumentTypeCommand command, CancellationToken cancellationToken = default) =>
        this.ToCatalogCreated(await mediator.Send(command, cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<ProductDocumentTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductDocumentTypeCommand command, CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await mediator.Send(command with { Id = id }, cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await mediator.Send(new DeleteProductDocumentTypeCommand(id), cancellationToken));

    /// <summary>Restores a logically deleted row (SPEC 41-post). Restricted to the SuperAdmin role.</summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, [FromQuery] Guid? companyId = null, CancellationToken cancellationToken = default) =>
        this.ToRestoreResult(await mediator.Send(new RestoreProductDocumentTypeCommand(id, companyId), cancellationToken));
}
