using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.CreateTicketUserCompany;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.DeleteTicketUserCompany;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.UpdateTicketUserCompany;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetSystemWideTicketUserCompanies;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetTicketUserCompanies;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetTicketUserCompanyById;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Messaging;

/// <summary>
/// Exposes REST endpoints for managing the tenant-scoped ticket roster
/// (the <c>User</c> ↔ <c>Company</c> link with ticket-management capabilities).
/// Tenant-scoped authorization lives in the flags attached to the
/// <c>TicketUserCompanies</c> permission resource; only the cross-tenant
/// <c>system-wide</c> endpoint is gated by the <c>SuperAdmin</c> role.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("TicketUserCompanies")]
public class TicketUserCompaniesController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Retrieves a paginated, tenant-scoped list of ticket-roster entries with optional capability filters.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<TicketUserCompanyDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] bool? isSuperAdminTicket = null,
        [FromQuery] bool? canFinishTicket = null,
        [FromQuery] bool? canResolveTicket = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(
            new GetTicketUserCompaniesQuery
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                UserId = userId,
                IsSuperAdminTicket = isSuperAdminTicket,
                CanFinishTicket = canFinishTicket,
                CanResolveTicket = canResolveTicket
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a paginated cross-tenant list of ticket-roster entries.
    /// Access is restricted to <c>SuperAdmin</c> users only.
    /// </summary>
    [HttpGet("system-wide")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<PagedResult<TicketUserCompanyDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSystemWide(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] bool? isSuperAdminTicket = null,
        [FromQuery] bool? canFinishTicket = null,
        [FromQuery] bool? canResolveTicket = null,
        [FromQuery] string? companyName = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(
            new GetSystemWideTicketUserCompaniesQuery
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                UserId = userId,
                IsSuperAdminTicket = isSuperAdminTicket,
                CanFinishTicket = canFinishTicket,
                CanResolveTicket = canResolveTicket,
                CompanyName = companyName
            },
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a single ticket-roster entry by its unique identifier (tenant-scoped).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<TicketUserCompanyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketUserCompanyByIdQuery(id), cancellationToken);

        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Adds a user to the tenant's ticket roster with the supplied capability flags.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<TicketUserCompanyDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTicketUserCompanyCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message is "COMPANY_REQUIRED")
            {
                return Unauthorized(response);
            }

            if (response.Message == "TICKET_USER_COMPANY_DUPLICATE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates the capability flags of an existing ticket-roster entry.
    /// The route id is the authoritative resource key; the payload cannot reassign the user.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<TicketUserCompanyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateTicketUserCompanyCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<TicketUserCompanyDto>.Error(
                "INVALID_TICKET_USER_COMPANY_ID",
                ["Route id and payload id must match."]));
        }

        var request = command with { Id = id };
        var response = await _sender.Send(request, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_REQUIRED")
            {
                return Unauthorized(response);
            }

            if (response.Message == "TICKET_USER_COMPANY_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "LAST_SUPERADMIN_TICKET")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Soft-deletes a ticket-roster entry. The handler enforces the
    /// "at least one active IsSuperAdminTicket" invariant.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DeleteTicketUserCompanyCommand(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_REQUIRED")
            {
                return Unauthorized(response);
            }

            if (response.Message == "TICKET_USER_COMPANY_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "LAST_SUPERADMIN_TICKET")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
