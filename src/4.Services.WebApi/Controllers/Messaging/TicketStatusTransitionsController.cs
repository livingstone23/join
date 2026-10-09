using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Commands.CreateTicketStatusTransition;
using JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Commands.DeleteTicketStatusTransition;
using JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Commands.RestoreTicketStatusTransition;
using JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Queries.GetTicketStatusTransitions;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Messaging;

/// <summary>
/// Exposes REST endpoints for managing the optional per-tenant ticket status transition rules
/// (SPEC 37). <c>Create</c> and <c>Delete</c> are the only write operations — by design there
/// is no <c>Update</c>, because the (From, To) pair is the rule's identity.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("TicketStatusTransitions")]
public class TicketStatusTransitionsController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Retrieves the list of transition rules configured for the current tenant.
    /// Optionally filters by the source status.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<IEnumerable<TicketStatusTransitionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? fromStatusId = null,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(
            new GetTicketStatusTransitionsQuery { FromStatusId = fromStatusId, IncludeDeleted = includeDeleted, CompanyId = companyId },
            cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_REQUIRED")
            {
                return Unauthorized(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Registers a new allowed transition between two ticket statuses for the current tenant.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<TicketStatusTransitionDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTicketStatusTransitionDto request,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateTicketStatusTransitionCommand
        {
            FromStatusId = request.FromStatusId,
            ToStatusId = request.ToStatusId
        };

        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_REQUIRED")
            {
                return Unauthorized(response);
            }

            if (response.Message == "TICKET_STATUS_TRANSITION_DUPLICATE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// Soft-deletes a single transition rule for the current tenant.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DeleteTicketStatusTransitionCommand(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_REQUIRED")
            {
                return Unauthorized(response);
            }

            if (response.Message == "TICKET_STATUS_TRANSITION_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Restores a logically deleted ticket status transition (SPEC 41). Restricted to the SuperAdmin role.
    /// </summary>
    /// <param name="id">The ticket status transition identifier.</param>
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
        var response = await _sender.Send(new RestoreTicketStatusTransitionCommand(id, companyId), cancellationToken);
        return this.ToRestoreResult(response);
    }
}
