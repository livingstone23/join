using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.UseCases.Customers.LeadSettings.ContactTypes.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.ContactTypes.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Customers.LeadSettings;

/// <summary>
/// Global master of contact types (SPEC 59). SuperAdmin only; companies edit their copy in
/// <c>/ContactTypeCompanies</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("LeadSettings")]
[Authorize(Roles = "SuperAdmin")]
public class ContactTypesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyList<ContactTypeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? includeInactive = null,
        [FromQuery] bool? includeDeleted = null,
        CancellationToken cancellationToken = default) =>
        this.ToLeadSettingsResult(await mediator.Send(new GetContactTypesQuery(includeInactive, includeDeleted), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<ContactTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] bool? includeDeleted = null, CancellationToken cancellationToken = default) =>
        this.ToLeadSettingsResult(await mediator.Send(new GetContactTypeByIdQuery(id, includeDeleted), cancellationToken));

    /// <summary>Adds a master type; every live company receives it inactive.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<ContactTypeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateContactTypeCommand command, CancellationToken cancellationToken = default) =>
        this.ToLeadSettingsCreated(await mediator.Send(command, cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<ContactTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateContactTypeCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<ContactTypeDto>.Error("INVALID_CONTACT_TYPE_ID", ["Route id and payload id must match."]));
        }

        return this.ToLeadSettingsResult(await mediator.Send(command with { Id = id }, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToLeadSettingsResult(await mediator.Send(new DeleteContactTypeCommand(id), cancellationToken));
}
