using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.CreateTicketUserCompany;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.DeleteTicketUserCompany;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.RestoreTicketUserCompany;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.UpdateTicketUserCompany;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetAssignableTicketUsers;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetMyTicketCapabilities;
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
    /// Returns the caller's ticket-management capabilities for the active
    /// tenant (SPEC 53). Reads the active company from the token, so the
    /// SuperAdmin company override is honored the same way every other
    /// tenant-scoped query honors it. A user outside the roster receives 200
    /// with <c>IsMember = false</c> and every capability flag <c>false</c>.
    /// Gated by <c>CanRead</c> on <c>Tickets</c>, not on
    /// <c>TicketUserCompanies</c> — anyone who works tickets needs this data.
    /// </summary>
    [HttpGet("me")]
    [PermissionResource("Tickets")]
    [ProducesResponseType(typeof(Response<TicketMyCapabilitiesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMine(
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetMyTicketCapabilitiesQuery(companyId), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_REQUIRED" || response.Message == "USER_REQUIRED")
            {
                return Unauthorized(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Returns the active users of the active tenant the caller may assign a
    /// ticket to (SPEC 53). The list mirrors the <c>CanResolveTicket = TRUE</c>
    /// + active filter that <c>CreateTicket</c> (initial assignee) and
    /// <c>ReassignTicket</c> (target) validate against, so the FE-17 "Asignar a"
    /// dropdown cannot offer a destination the backend would reject.
    /// Gated by <c>CanRead</c> on <c>Tickets</c>, not on
    /// <c>TicketUserCompanies</c> — the agent needs the list even without
    /// permission to administer the roster.
    /// </summary>
    [HttpGet("assignable")]
    [PermissionResource("Tickets")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<TicketAssignableUserDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAssignable(
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetAssignableTicketUsersQuery(companyId), cancellationToken);

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
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
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
                CanResolveTicket = canResolveTicket,
                IncludeDeleted = includeDeleted,
                CompanyId = companyId
            },
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
    public async Task<IActionResult> GetById(
        Guid id,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetTicketUserCompanyByIdQuery(id, includeDeleted, companyId), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_REQUIRED")
            {
                return Unauthorized(response);
            }

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
        [FromBody] CreateTicketUserCompanyDto request,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateTicketUserCompanyCommand
        {
            UserId = request.UserId,
            IsSuperAdminTicket = request.IsSuperAdminTicket,
            CanFinishTicket = request.CanFinishTicket,
            CanResolveTicket = request.CanResolveTicket
        };

        var response = await _sender.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_REQUIRED")
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
    /// The route id is the resource key; the payload carries only the flags, so the user cannot be reassigned.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<TicketUserCompanyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateTicketUserCompanyDto request,
        CancellationToken cancellationToken = default)
    {
        var command = new UpdateTicketUserCompanyCommand
        {
            Id = id,
            IsSuperAdminTicket = request.IsSuperAdminTicket,
            CanFinishTicket = request.CanFinishTicket,
            CanResolveTicket = request.CanResolveTicket
        };

        var response = await _sender.Send(command, cancellationToken);

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

    /// <summary>
    /// Restores a logically deleted ticket roster entry (SPEC 41). Restricted to the SuperAdmin role.
    /// </summary>
    /// <param name="id">The ticket roster entry identifier.</param>
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
        var response = await _sender.Send(new RestoreTicketUserCompanyCommand(id, companyId), cancellationToken);
        return this.ToRestoreResult(response);
    }
}
