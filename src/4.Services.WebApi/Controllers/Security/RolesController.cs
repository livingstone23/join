using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.DTO.Security.RoleUsers;
using JOIN.Application.UseCases.Security.Roles.Commands.CreateRole;
using JOIN.Application.UseCases.Security.Roles.Commands.DeleteRole;
using JOIN.Application.UseCases.Security.Roles.Commands.UpdateRole;
using JOIN.Application.UseCases.Security.Roles.Queries.GetRoleById;
using JOIN.Application.UseCases.Security.Roles.Queries.GetRolesDetailed;
using JOIN.Application.UseCases.Security.Roles.Queries.GetUsersByRoleId;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;



namespace JOIN.Services.WebApi.Controllers.Security;



/// <summary>
/// Exposes endpoints for the ApplicationRole catalog used by the security and administration modules.
/// Legacy <c>GET /Roles</c> returns the role-name list consumed by selectors.
/// Detailed, single-fetch, create, update, and soft-delete endpoints are routed through MediatR.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("Roles")]
public class RolesController(RoleManager<ApplicationRole> roleManager, IMediator mediator) : ControllerBase
{
    private readonly RoleManager<ApplicationRole> _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Returns the active role names registered in the Identity store, sorted alphabetically for predictable UI rendering.
    /// Consumed by role selectors in the assignment and permission-management screens.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the request while the role list is being materialized.</param>
    /// <returns>A standardized response containing the role names available to the application.</returns>
    [HttpGet]
    [Authorize(Roles = "SuperAdminCompany")]
    [ProducesResponseType(typeof(Response<IEnumerable<string>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
    {
        var roles = await _roleManager.Roles
            .OrderBy(role => role.Name)
            .Select(role => role.Name ?? string.Empty)
            .Where(roleName => roleName != string.Empty)
            .ToListAsync(cancellationToken);

        return Ok(new Response<IEnumerable<string>>
        {
            Data = roles,
            IsSuccess = true,
            Message = "Roles retrieved successfully."
        });
    }

    /// <summary>
    /// Returns a paged and filtered view of ApplicationRole rows including audit metadata.
    /// </summary>
    /// <param name="name">Optional case-preserving substring filter applied to <c>Name</c>.</param>
    /// <param name="isActive">Optional active flag. true filters GcRecord = 0; false filters GcRecord &lt;&gt; 0; null returns all.</param>
    /// <param name="page">1-based page number; clamped to &gt;= 1.</param>
    /// <param name="pageSize">Page size; clamped to [1, 100].</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>A standardized paged response containing matching role DTOs.</returns>
    [HttpGet("detailed")]
    [Authorize(Roles = "SuperAdminCompany")]
    [ProducesResponseType(typeof(Response<PagedResult<RoleDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Response<PagedResult<RoleDto>>>> GetDetailed(
        [FromQuery] string? name,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetRolesDetailedQuery(name, isActive, page, pageSize), cancellationToken);
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    /// <summary>
    /// Returns a single ApplicationRole projected to <see cref="RoleDto"/>.
    /// </summary>
    /// <param name="id">Unique identifier of the role.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// A standardized response containing the role DTO.
    /// Returns <c>404 Not Found</c> when the role does not exist or has been soft-deleted.
    /// </returns>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SuperAdminCompany")]
    [ProducesResponseType(typeof(Response<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Response<RoleDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetRoleByIdQuery(id), cancellationToken);
        if (!response.IsSuccess && response.Message == "Rol no encontrado o inactivo.")
        {
            return NotFound(response);
        }

        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    /// <summary>
    /// Returns a paged list of users currently assigned to the role, scoped to the caller's tenant.
    /// Powers the "usuarios afectados" preview shown by the UI before saving role changes.
    /// </summary>
    /// <param name="id">Unique identifier of the role.</param>
    /// <param name="page">1-based page number; clamped to &gt;= 1.</param>
    /// <param name="pageSize">Page size; clamped to [1, 100].</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// A standardized paged response containing matching users. <c>404 Not Found</c>
    /// when the role is missing or soft-deleted; <c>401 Unauthorized</c> when the
    /// caller's token lacks a CompanyId claim/header.
    /// </returns>
    [HttpGet("{id:guid}/users")]
    [Authorize(Roles = "SuperAdminCompany")]
    [ProducesResponseType(typeof(Response<PagedResult<RoleAffectedUserDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Response<PagedResult<RoleAffectedUserDto>>>> GetUsers(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetUsersByRoleIdQuery(id, page, pageSize), cancellationToken);

        if (response.IsSuccess) return Ok(response);

        return response.Message switch
        {
            "INVALID_COMPANY_ID" => Unauthorized(response),
            "Rol no encontrado o inactivo." => NotFound(response),
            _ => BadRequest(response)
        };
    }

    /// <summary>
    /// Creates a new ApplicationRole. The handler trims and upper-cases the supplied name before persistence.
    /// </summary>
    /// <param name="command">Payload with the display name, optional description, and system-default flag.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// A standardized response containing the created role DTO and a <c>Location</c> header pointing to the new endpoint.
    /// Returns <c>409 Conflict</c> when another active role already uses the same name.
    /// </returns>
    [HttpPost]
    [Authorize(Roles = "SuperAdminCompany")]
    [ProducesResponseType(typeof(Response<RoleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Response<RoleDto>>> Create(
        [FromBody] CreateRoleCommand command,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(command, cancellationToken);
        if (!response.IsSuccess && response.Message.StartsWith("Ya existe un rol con el nombre", StringComparison.Ordinal))
        {
            return Conflict(response);
        }

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates an existing ApplicationRole. System-default roles reject Name changes and flag demotion.
    /// </summary>
    /// <param name="id">Unique identifier of the role to update.</param>
    /// <param name="command">Payload with the new name, description, and system-default flag.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// A standardized response containing the updated role DTO.
    /// Returns <c>404 Not Found</c> when the role does not exist or has been soft-deleted.
    /// Returns <c>409 Conflict</c> when the new name collides with another role.
    /// </returns>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdminCompany")]
    [ProducesResponseType(typeof(Response<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Response<RoleDto>>> Update(
        Guid id,
        [FromBody] UpdateRoleCommand command,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(command with { Id = id }, cancellationToken);
        if (!response.IsSuccess && response.Message == "Rol no encontrado o inactivo.")
        {
            return NotFound(response);
        }

        if (!response.IsSuccess && response.Message.StartsWith("Ya existe otro rol con el nombre", StringComparison.Ordinal))
        {
            return Conflict(response);
        }

        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    /// <summary>
    /// Soft-deletes an ApplicationRole. System-default roles are rejected with <c>403 Forbidden</c>.
    /// </summary>
    /// <param name="id">Unique identifier of the role to soft-delete.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdminCompany")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new DeleteRoleCommand(id), cancellationToken);
        if (response.IsSuccess)
        {
            return NoContent();
        }

        if (response.Message == "Rol no encontrado o inactivo.")
        {
            return NotFound(response);
        }

        if (response.Message.StartsWith("No se puede eliminar un rol del sistema", StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, response);
        }

        if (response.Message == "ROLE_HAS_USERS")
        {
            return Conflict(response);
        }

        return BadRequest(response);
    }
}
