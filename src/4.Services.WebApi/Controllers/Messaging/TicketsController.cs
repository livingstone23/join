using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.Tickets.Commands;
using JOIN.Application.UseCases.Messaging.Tickets.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Messaging;

/// <summary>
/// Exposes REST endpoints for ticket management.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[PermissionResource("Tickets")]
public class TicketsController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Retrieves a paginated list of tickets in the current tenant with optional filters.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<TicketListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null,
        [FromQuery] Guid? ticketStatusId = null,
        [FromQuery] Guid? ticketComplexityId = null,
        [FromQuery] Guid? assignedToUserId = null,
        [FromQuery] Guid? customerId = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] bool? isVisibleToExternals = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetTicketsQuery(
            pageNumber,
            pageSize,
            search,
            ticketStatusId,
            ticketComplexityId,
            assignedToUserId,
            customerId,
            projectId,
            isVisibleToExternals,
            fromDate,
            toDate);

        var response = await _sender.Send(query, cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a paginated system-wide list of tickets across all companies.
    /// Access is restricted to SuperAdmin users only.
    /// </summary>
    [HttpGet("system-wide")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<PagedResult<TicketListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSystemWide(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? search = null,
        [FromQuery] Guid? ticketStatusId = null,
        [FromQuery] Guid? ticketComplexityId = null,
        [FromQuery] Guid? assignedToUserId = null,
        [FromQuery] Guid? customerId = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] bool? isVisibleToExternals = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? companyName = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetSystemWideTicketsQuery(
            pageNumber,
            pageSize,
            search,
            ticketStatusId,
            ticketComplexityId,
            assignedToUserId,
            customerId,
            projectId,
            isVisibleToExternals,
            fromDate,
            toDate,
            companyName);

        var response = await _sender.Send(query, cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a single ticket by identifier for the current tenant.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<TicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketByIdQuery(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "TICKET_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new ticket in the current tenant.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<TicketDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateTicketDto dto, CancellationToken cancellationToken = default)
    {
        var command = new CreateTicketCommand
        {
            Name = dto.Name,
            Description = dto.Description,
            EstimatedTime = dto.EstimatedTime,
            ConsumedTime = dto.ConsumedTime,
            EffortPoints = dto.EffortPoints,
            IsVisibleToExternals = dto.IsVisibleToExternals,
            TicketStatusId = dto.TicketStatusId,
            TicketComplexityId = dto.TicketComplexityId,
            TimeUnitId = dto.TimeUnitId,
            PersonId = dto.PersonId,
            ProjectId = dto.ProjectId,
            AreaId = dto.AreaId,
            ChannelId = dto.ChannelId,
            AssignedToUserId = dto.AssignedToUserId,
            PrecedentTicketId = dto.PrecedentTicketId
        };

        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "TICKET_CODE_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates a ticket by identifier in the current tenant.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<TicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateTicketDto dto,
        CancellationToken cancellationToken = default)
    {
        var command = new UpdateTicketCommand
        {
            Id = id,
            Name = dto.Name,
            Description = dto.Description,
            EstimatedTime = dto.EstimatedTime,
            ConsumedTime = dto.ConsumedTime,
            EffortPoints = dto.EffortPoints,
            IsVisibleToExternals = dto.IsVisibleToExternals,
            TicketStatusId = dto.TicketStatusId,
            TicketComplexityId = dto.TicketComplexityId,
            TimeUnitId = dto.TimeUnitId,
            PersonId = dto.PersonId,
            ProjectId = dto.ProjectId,
            AreaId = dto.AreaId,
            ChannelId = dto.ChannelId,
            AssignedToUserId = dto.AssignedToUserId,
            PrecedentTicketId = dto.PrecedentTicketId
        };

        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "TICKET_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a logical delete of a ticket by identifier in the current tenant.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DeleteTicketCommand(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "TICKET_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
