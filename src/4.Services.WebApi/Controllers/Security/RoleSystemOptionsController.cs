using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace JOIN.Services.WebApi.Controllers.Security;

/// <summary>
/// Exposes REST endpoints to manage granular role permissions over system options.
/// This controller delegates all business logic to MediatR handlers and only maps
/// application responses into HTTP status codes.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("RoleSystemOption")]
public class RoleSystemOptionsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Retrieves a single permission rule by its unique identifier.
    /// The read is tenant-scoped and returns the resolved role name, system option name,
    /// and company name when the record exists.
    /// </summary>
    /// <param name="id">Unique identifier of the permission rule.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// A standardized response containing the permission rule when found.
    /// Returns <c>404 Not Found</c> when the rule does not exist for the current scope.
    /// </returns>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Response<RoleSystemOptionDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var response = await mediator.Send(new GetRoleSystemOptionByIdQuery(id), cancellationToken);
        if (!response.IsSuccess && response.Message == "ROLE_SYSTEM_OPTION_NOT_FOUND")
        {
            return NotFound(response);
        }

        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    /// <summary>
    /// Retrieves a tenant-scoped paged list of permission rules.
    /// Supports optional inclusive filters by role, system option, display names, and permission flags.
    /// </summary>
    /// <param name="pageNumber">Optional 1-based page number.</param>
    /// <param name="pageSize">Optional page size.</param>
    /// <param name="roleId">Optional exact filter by role identifier.</param>
    /// <param name="systemOptionId">Optional exact filter by system option identifier.</param>
    /// <param name="roleName">Optional partial-match filter for role name.</param>
    /// <param name="systemOptionName">Optional partial-match filter for system option name.</param>
    /// <param name="companyName">Optional partial-match filter for company name.</param>
    /// <param name="canRead">Optional exact filter by read permission flag.</param>
    /// <param name="canCreate">Optional exact filter by create permission flag.</param>
    /// <param name="canUpdate">Optional exact filter by update permission flag.</param>
    /// <param name="canDelete">Optional exact filter by delete permission flag.</param>
    /// <param name="canDownload">Optional exact filter by download permission flag.</param>
    /// <param name="canExport">Optional exact filter by export permission flag.</param>
    /// <param name="canExecute">Optional exact filter by execute permission flag.</param>
    /// <param name="isVisibleMenu">Optional exact filter by menu visibility flag.</param>
    /// <param name="orderMenu">Optional exact filter by menu ordering index.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>A standardized paged response with matching permission rules.</returns>
    [HttpGet]
    public async Task<ActionResult<Response<PagedResult<RoleSystemOptionListItemDto>>>> GetPaged(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] Guid? roleId = null,
        [FromQuery] Guid? systemOptionId = null,
        [FromQuery] string? roleName = null,
        [FromQuery] string? systemOptionName = null,
        [FromQuery] string? companyName = null,
        [FromQuery] bool? canRead = null,
        [FromQuery] bool? canCreate = null,
        [FromQuery] bool? canUpdate = null,
        [FromQuery] bool? canDelete = null,
        [FromQuery] bool? canDownload = null,
        [FromQuery] bool? canExport = null,
        [FromQuery] bool? canExecute = null,
        [FromQuery] bool? isVisibleMenu = null,
        [FromQuery] int? orderMenu = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetRoleSystemOptionsPagedQuery(
            pageNumber,
            pageSize,
            roleId,
            systemOptionId,
            roleName,
            systemOptionName,
            companyName,
            canRead,
            canCreate,
            canUpdate,
            canDelete,
            canDownload,
            canExport,
            canExecute,
            isVisibleMenu,
            orderMenu);

        var response = await mediator.Send(query, cancellationToken);
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    /// <summary>
    /// Creates a new permission rule.
    /// The request body must include <c>companyId</c>, <c>roleId</c>, <c>systemOptionId</c>,
    /// and the four CRUD permission flags.
    /// </summary>
    /// <param name="command">Payload with company, role, system option, and permission flags.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// A standardized response containing the created permission rule.
    /// Returns <c>409 Conflict</c> when an equivalent active rule already exists.
    /// </returns>
    [HttpPost]
    public async Task<ActionResult<Response<RoleSystemOptionDto>>> Create(
        [FromBody] CreateRoleSystemOptionCommand command,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(command, cancellationToken);
        if (!response.IsSuccess && response.Message == "ROLE_SYSTEM_OPTION_ALREADY_EXISTS")
        {
            return Conflict(response);
        }

        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    /// <summary>
    /// Updates permission flags of an existing rule.
    /// The route provides the rule identifier; the tenant is always derived from the authenticated
    /// caller's JWT (via <c>ICurrentUserService</c>). The body may optionally carry <c>companyId</c> for
    /// backward compatibility — when present it must match the authenticated tenant.
    /// </summary>
    /// <param name="id">Unique identifier of the permission rule to update.</param>
    /// <param name="command">Payload with the updated flags. <c>companyId</c> is optional.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// A standardized response containing the updated permission rule.
    /// Returns <c>404 Not Found</c> when the target rule is not found for the caller's tenant.
    /// </returns>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Response<RoleSystemOptionDto>>> Update(
        Guid id,
        [FromBody] UpdateRoleSystemOptionCommand command,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(command with { Id = id }, cancellationToken);
        if (!response.IsSuccess && response.Message == "ROLE_SYSTEM_OPTION_NOT_FOUND")
        {
            return NotFound(response);
        }

        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    /// <summary>
    /// Performs a soft delete of a permission rule.
    /// The tenant is always derived from the authenticated caller's JWT (via <c>ICurrentUserService</c>).
    /// </summary>
    /// <param name="id">Unique identifier of the permission rule to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// A standardized response containing the deleted rule identifier.
    /// Returns <c>404 Not Found</c> when the rule does not exist for the caller's tenant.
    /// </returns>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<Response<Guid>>> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(new DeleteRoleSystemOptionCommand(id), cancellationToken);
        if (!response.IsSuccess && response.Message == "ROLE_SYSTEM_OPTION_NOT_FOUND")
        {
            return NotFound(response);
        }

        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }

    /// <summary>
    /// Retrieves paged permission rules across all companies for SuperAdmin users.
    /// Accepts the same filters as <see cref="GetPaged"/>, and optionally restricts results
    /// to a single company through <paramref name="companyId"/>.
    /// </summary>
    /// <param name="pageNumber">Optional 1-based page number.</param>
    /// <param name="pageSize">Optional page size.</param>
    /// <param name="roleId">Optional exact filter by role identifier.</param>
    /// <param name="systemOptionId">Optional exact filter by system option identifier.</param>
    /// <param name="roleName">Optional partial-match filter for role name.</param>
    /// <param name="systemOptionName">Optional partial-match filter for system option name.</param>
    /// <param name="companyName">Optional partial-match filter for company name.</param>
    /// <param name="canRead">Optional exact filter by read permission flag.</param>
    /// <param name="canCreate">Optional exact filter by create permission flag.</param>
    /// <param name="canUpdate">Optional exact filter by update permission flag.</param>
    /// <param name="canDelete">Optional exact filter by delete permission flag.</param>
    /// <param name="canDownload">Optional exact filter by download permission flag.</param>
    /// <param name="canExport">Optional exact filter by export permission flag.</param>
    /// <param name="canExecute">Optional exact filter by execute permission flag.</param>
    /// <param name="isVisibleMenu">Optional exact filter by menu visibility flag.</param>
    /// <param name="orderMenu">Optional exact filter by menu ordering index.</param>
    /// <param name="companyId">Optional exact filter by company identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>A standardized paged response containing cross-company permission rules.</returns>
    [HttpGet("superadmin/all")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<Response<PagedResult<RoleSystemOptionListItemDto>>>> GetSuperAdminPaged(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] Guid? roleId = null,
        [FromQuery] Guid? systemOptionId = null,
        [FromQuery] string? roleName = null,
        [FromQuery] string? systemOptionName = null,
        [FromQuery] string? companyName = null,
        [FromQuery] bool? canRead = null,
        [FromQuery] bool? canCreate = null,
        [FromQuery] bool? canUpdate = null,
        [FromQuery] bool? canDelete = null,
        [FromQuery] bool? canDownload = null,
        [FromQuery] bool? canExport = null,
        [FromQuery] bool? canExecute = null,
        [FromQuery] bool? isVisibleMenu = null,
        [FromQuery] int? orderMenu = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetSuperAdminAllRoleSystemOptionsPagedQuery(
            pageNumber,
            pageSize,
            roleId,
            systemOptionId,
            roleName,
            systemOptionName,
            companyName,
            canRead,
            canCreate,
            canUpdate,
            canDelete,
            canDownload,
            canExport,
            canExecute,
            isVisibleMenu,
            orderMenu,
            companyId);

        var response = await mediator.Send(query, cancellationToken);
        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }
}
