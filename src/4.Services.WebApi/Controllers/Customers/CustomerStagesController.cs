using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;
using JOIN.Application.UseCases.Customers.CustomerStages.Commands;
using JOIN.Application.UseCases.Customers.CustomerStages.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Customers;

/// <summary>
/// The company's customer stage catalog (SPEC 55 part F).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CustomerStages")]
public class CustomerStagesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyList<CustomerStageDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? includeInactive = null,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default) =>
        this.ToResult(await mediator.Send(new GetCustomerStagesQuery(includeInactive, includeDeleted, companyId), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CustomerStageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default) =>
        this.ToResult(await mediator.Send(new GetCustomerStageByIdQuery(id, includeDeleted, companyId), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(Response<CustomerStageDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCustomerStageCommand command, CancellationToken cancellationToken = default) =>
        this.ToCreated(await mediator.Send(command, cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CustomerStageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCustomerStageCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<CustomerStageDto>.Error("INVALID_CUSTOMER_STAGE_ID", ["Route id and payload id must match."]));
        }

        return this.ToResult(await mediator.Send(command with { Id = id }, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await mediator.Send(new DeleteCustomerStageCommand(id), cancellationToken));

    /// <summary>
    /// Restores a logically deleted stage (SPEC 41-post). Restricted to the SuperAdmin role.
    /// </summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, [FromQuery] Guid? companyId = null, CancellationToken cancellationToken = default) =>
        this.ToRestoreResult(await mediator.Send(new RestoreCustomerStageCommand(id, companyId), cancellationToken));
}
