using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.UseCases.Catalog.ProductAttachmentSettings.Commands;
using JOIN.Application.UseCases.Catalog.ProductAttachmentSettings.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Catalog;

/// <summary>
/// Attachment limits of the token's company for products and services (SPEC 70), "Adjuntos" tab of the product
/// settings. Resource <c>ProductSettings</c> (rule 5): who edits products does not change the limits.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("ProductSettings")]
public class ProductAttachmentSettingsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<ProductAttachmentSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await mediator.Send(new GetProductAttachmentSettingsQuery(), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(Response<ProductAttachmentSettingsDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateProductAttachmentSettingsCommand command, CancellationToken cancellationToken = default) =>
        this.ToCatalogCreated(await mediator.Send(command, cancellationToken));

    [HttpPut]
    [ProducesResponseType(typeof(Response<ProductAttachmentSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromBody] UpdateProductAttachmentSettingsCommand command, CancellationToken cancellationToken = default) =>
        this.ToCatalogResult(await mediator.Send(command, cancellationToken));
}
