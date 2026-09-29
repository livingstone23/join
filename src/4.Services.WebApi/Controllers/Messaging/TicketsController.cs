using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.Tickets.Commands;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.AddTicketNote;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.FinishTicket;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.ReassignTicket;
using JOIN.Application.UseCases.Messaging.Tickets.Queries;
using JOIN.Domain.Enums;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace JOIN.Services.WebApi.Controllers.Messaging;

/// <summary>
/// Exposes REST endpoints for ticket management.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
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
            return MapResponseError(response);
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
            return MapResponseError(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a single ticket by identifier for the current tenant.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<TicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketByIdQuery(id), cancellationToken);

        if (!response.IsSuccess)
        {
            return MapResponseError(response);
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
            return MapResponseError(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates a ticket by identifier in the current tenant.
    /// Reassignment is intentionally NOT supported on this endpoint (SPEC 35);
    /// use <c>PUT /Tickets/{id}/reassign</c> to change the assignee.
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
            PrecedentTicketId = dto.PrecedentTicketId
        };

        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return MapResponseError(response);
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
            return MapResponseError(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Reassigns a ticket to a different user (SPEC 35). Covers both the first
    /// assignment of a previously-unassigned ticket and the reassignment of one
    /// already assigned. The actor must hold the <c>IsSuperAdminTicket</c>
    /// capability for the tenant; the target must hold <c>CanResolveTicket</c>.
    /// Reassigning to the same user already on the ticket is a successful no-op
    /// that does not emit a new <c>Reassignment</c> log entry.
    /// </summary>
    [HttpPut("{id:guid}/reassign")]
    [ProducesResponseType(typeof(Response<TicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reassign(
        Guid id,
        [FromBody] ReassignTicketDto dto,
        CancellationToken cancellationToken = default)
    {
        var command = new ReassignTicketCommand
        {
            TicketId = id,
            NewAssignedToUserId = dto.NewAssignedToUserId
        };

        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return MapResponseError(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Finalizes a ticket by transitioning it to a status with
    /// <c>IsFinal = true</c> (SPEC 35). The actor must hold either
    /// <c>CanFinishTicket</c> or <c>IsSuperAdminTicket</c>. A ticket already
    /// in a final status cannot be re-finalized through this endpoint
    /// (<c>TICKET_ALREADY_FINISHED</c>, 409).
    /// </summary>
    [HttpPut("{id:guid}/finish")]
    [ProducesResponseType(typeof(Response<TicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Finish(
        Guid id,
        [FromBody] FinishTicketDto dto,
        CancellationToken cancellationToken = default)
    {
        var command = new FinishTicketCommand
        {
            TicketId = id,
            TicketStatusId = dto.TicketStatusId,
            ResolutionSummary = dto.ResolutionSummary
        };

        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return MapResponseError(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Appends an internal or external note to a ticket (SPEC 35). Visibility
    /// is derived server-side from the supplied <see cref="LogType"/>:
    /// <c>InternalNote</c> is hidden from third viewers (creator/assignee/
    /// super-admin only), <c>ExternalNote</c> is public. The wire DTO carries
    /// the type as an <see cref="int"/> to keep the webapi surface free of
    /// domain references; only <c>2</c> (InternalNote) and <c>3</c> (ExternalNote)
    /// are accepted by the validator.
    /// </summary>
    [HttpPost("{id:guid}/notes")]
    [ProducesResponseType(typeof(Response<TicketLogDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddNote(
        Guid id,
        [FromBody] AddTicketNoteDto dto,
        CancellationToken cancellationToken = default)
    {
        var command = new AddTicketNoteCommand
        {
            TicketId = id,
            LogType = (LogType)dto.LogType,
            Summary = dto.Summary
        };

        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return MapResponseError(response);
        }

        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// Maps a failure <see cref="Response{T}"/> from the application layer to
    /// the HTTP status documented in SPEC 35. Centralizes the business-code
    /// → status mapping so every action in this controller (preexisting and
    /// new) reports failures consistently. In particular,
    /// <c>COMPANY_REQUIRED</c> always returns <c>401</c> rather than the
    /// <c>400</c> that a <c>BadRequest</c> fallback would produce — closing
    /// the cross-endpoint incoherence that existed before this spec.
    /// </summary>
    private IActionResult MapResponseError<T>(Response<T> response)
    {
        return response.Message switch
        {
            "COMPANY_REQUIRED" or "USER_REQUIRED"
                => Unauthorized(response),
            "TICKET_NOT_FOUND"
                => NotFound(response),
            "TICKET_REASSIGN_FORBIDDEN" or "TICKET_FINISH_FORBIDDEN"
                => StatusCode(StatusCodes.Status403Forbidden, response),
            "TICKET_ALREADY_FINISHED" or "TICKET_CODE_IN_USE"
                => Conflict(response),
            _ => BadRequest(response)
        };
    }
}