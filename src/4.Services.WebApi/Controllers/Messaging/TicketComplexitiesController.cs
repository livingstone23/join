using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.TicketComplexities.Commands;
using JOIN.Application.UseCases.Messaging.TicketComplexities.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using JOIN.Services.WebApi.Controllers;



namespace JOIN.Services.WebApi.Controllers.Messaging;



/// <summary>
/// Exposes REST endpoints for managing the global ticket complexity catalog.
/// The controller remains intentionally thin and delegates business rules to the Application layer through MediatR.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("TicketComplexities")]
public class TicketComplexitiesController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Retrieves a paginated list of ticket complexities with optional filters by name and active status.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<TicketComplexityDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? name = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketComplexitiesQuery(pageNumber, pageSize, name, isActive, includeDeleted, companyId), cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a paginated system-wide list of ticket complexities across all companies.
    /// Access is restricted to SuperAdmin users only.
    /// </summary>
    [HttpGet("system-wide")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<PagedResult<TicketComplexityDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSystemWide(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? name = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? companyName = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(
            new GetSystemWideTicketComplexitiesQuery(pageNumber, pageSize, name, isActive, companyName),
            cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a single ticket complexity by its unique identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<TicketComplexityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] bool? includeDeleted = null, [FromQuery] Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketComplexityByIdQuery(id, includeDeleted, companyId), cancellationToken);

        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new ticket complexity catalog entry.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<TicketComplexityDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateTicketComplexityCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message is "TICKET_COMPLEXITY_NAME_IN_USE" or "TICKET_COMPLEXITY_CODE_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates an existing ticket complexity using the route identifier as the authoritative resource key.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<TicketComplexityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTicketComplexityCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<TicketComplexityDto>.Error("INVALID_TICKET_COMPLEXITY_ID", ["Route id and payload id must match."]));
        }

        var request = command with { Id = id };
        var response = await _sender.Send(request, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "TICKET_COMPLEXITY_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message is "TICKET_COMPLEXITY_NAME_IN_USE" or "TICKET_COMPLEXITY_CODE_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete over a ticket complexity catalog entry.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DeleteTicketComplexityCommand(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "TICKET_COMPLEXITY_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "TICKET_COMPLEXITY_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Restores a logically deleted ticket complexity (SPEC 41). Restricted to the SuperAdmin role.
    /// </summary>
    /// <param name="id">The ticket complexity identifier.</param>
    /// <param name="companyId">Optional explicit company, honored only for SuperAdmin.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(
        Guid id,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new RestoreTicketComplexityCommand(id, companyId), cancellationToken);
        return this.ToRestoreResult(response);
    }
}
