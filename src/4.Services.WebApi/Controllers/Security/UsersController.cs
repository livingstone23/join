using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.DTO.Security.User;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Refresh;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Application.UseCases.Security.Queries.GetMyCompanyUserReport;
using JOIN.Application.UseCases.Security.Queries.GetSidebarMenu;
using JOIN.Application.UseCases.Security.Queries.GetSystemWideUserReport;
using JOIN.Application.UseCases.Security.UserCompanies.Commands.AddUserCompany;
using JOIN.Application.UseCases.Security.UserCompanies.Commands.RemoveUserCompany;
using JOIN.Application.UseCases.Security.UserCompanies.Commands.SetDefaultCompany;
using JOIN.Application.UseCases.Security.UserCompanies.Queries.GetUserCompanies;
using JOIN.Application.UseCases.Security.Users.Commands.BulkUpdateUserRoles;
using JOIN.Application.UseCases.Security.Users.Commands.ChangeUserStatus;
using JOIN.Application.UseCases.Security.Users.Commands.ForceUserPasswordReset;
using JOIN.Application.UseCases.Security.Users.Commands.InvalidateSidebarCache;
using JOIN.Application.UseCases.Security.Users.Commands.InviteUser;
using JOIN.Application.UseCases.Security.Users.Commands.ReplaceUserRoles;
using JOIN.Application.UseCases.Security.Users.Queries.GetUserEffectivePermissions;
using JOIN.Application.UseCases.Security.Users.Queries.GetUsersWithRoles;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;



namespace JOIN.Services.WebApi.Controllers.Security;



/// <summary>
/// Exposes authentication, user-administration, reporting, and user-to-company assignment endpoints for the security module.
/// The controller keeps a thin HTTP-facing role and delegates business logic and data access to the Application layer through MediatR.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("Users")]
public class UsersController(IMediator mediator) : ControllerBase
{



    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));



    /// <summary>
    /// Authenticates a user and returns the complete JWT session payload, including the effective company context, role list, access token, refresh token, and expiration metadata.
    /// This endpoint is the entry point for establishing an authenticated session in the security module.
    /// </summary>
    /// <param name="command">The login payload containing the user credentials and optional target company context.</param>
    /// <param name="cancellationToken">Token used to cancel the authentication request while the login command is being processed.</param>
    /// <returns>The authenticated session payload when the credentials are valid.</returns>
    [AllowAnonymous]
    [EnableRateLimiting("Strict")]
    [HttpPost("login")]
    [ProducesResponseType(typeof(Response<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(new Response<LoginResponse>
        {
            IsSuccess = true,
            Data = response
        });
    }



    /// <summary>
    /// Registers a new active user account without initial company assignments.
    /// The actual validation and persistence rules are executed in the Application layer through the registration command handler.
    /// </summary>
    /// <param name="command">The registration payload containing the new user data.</param>
    /// <param name="cancellationToken">Token used to cancel the registration request while the command is being handled.</param>
    /// <returns>A standardized response containing the identifier of the newly registered user.</returns>
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(Response<RegisterResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }



    /// <summary>
    /// Rotates a valid refresh token and returns a renewed authenticated session payload.
    /// This endpoint is intended to extend a user session without forcing the user to re-enter credentials.
    /// </summary>
    /// <param name="command">The refresh-token payload used to request a renewed session.</param>
    /// <param name="cancellationToken">Token used to cancel the refresh request while the command is being handled.</param>
    /// <returns>The renewed authenticated session payload when the refresh token is valid.</returns>
    [AllowAnonymous]
    [EnableRateLimiting("Strict")]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(Response<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(new Response<LoginResponse>
        {
            IsSuccess = true,
            Data = response
        });
    }



    /// <summary>
    /// Terminates the current authenticated session in the server-side authentication pipeline.
    /// This endpoint is used to explicitly close the active sign-in context associated with the caller.
    /// </summary>
    /// <returns>A standardized success response indicating that the logout operation completed successfully.</returns>
    [HttpPost("logout")]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

        return Ok(new Response<object>
        {
            IsSuccess = true,
            Message = "Logout completed successfully."
        });
    }



    /// <summary>
    /// Returns the user management and activity report across all companies in the system.
    /// Access is restricted to <c>SuperAdmin</c> users because the response spans multiple tenants and may include cross-company activity information.
    /// </summary>
    /// <param name="fromDate">Optional lower UTC date boundary used to limit the report window.</param>
    /// <param name="toDate">Optional upper UTC date boundary used to limit the report window.</param>
    /// <param name="targetCompanyId">Optional explicit company filter applied inside the system-wide report.</param>
    /// <param name="roleNames">Optional list of role names used to restrict the users included in the report.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the report query executes.</param>
    /// <returns>A standardized response containing the requested system-wide user management report rows.</returns>
    [HttpGet("reports/system")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<IReadOnlyCollection<UserManagementReportDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSystemWideReport(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] Guid? targetCompanyId = null,
        [FromQuery] string[]? roleNames = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetSystemWideUserReportQuery(fromDate, toDate, targetCompanyId, roleNames),
            cancellationToken);

        return Ok(response);
    }



    /// <summary>
    /// Returns a paginated slice of the user management and activity report restricted to the
    /// authenticated user's active company context. SPEC 28 / F7 (item 21).
    /// The response shape changed from a flat collection to <c>PagedResult&lt;T&gt;</c> —
    /// coordinate the deploy with the front-end consumer.
    /// </summary>
    /// <param name="pageNumber">1-based page index; values below 1 are clamped to 1.</param>
    /// <param name="pageSize">Items per page; values outside <c>[1, 50]</c> are clamped (default 10).</param>
    /// <param name="search">Optional partial match against Email or full name, case-insensitive.</param>
    /// <param name="isActive">Optional active/inactive filter; omit to include both.</param>
    /// <param name="fromDate">Optional lower UTC date boundary used to filter the report window.</param>
    /// <param name="toDate">Optional upper UTC date boundary used to filter the report window.</param>
    /// <param name="roleNames">Optional list of role names used to narrow the report result set.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the company-scoped report query runs.</param>
    /// <returns>A standardized paginated response containing the requested user management rows.</returns>
    [HttpGet("reports/my-company")]
    [ProducesResponseType(typeof(Response<PagedResult<UserManagementReportDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyCompanyReport(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string[]? roleNames = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetMyCompanyUserReportQuery(
                fromDate, toDate, roleNames,
                pageNumber, pageSize,
                search, isActive),
            cancellationToken);

        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }



    /// <summary>
    /// Returns the hierarchical sidebar menu resolved for the authenticated user in the active company context.
    /// This endpoint intentionally skips the controller-based dynamic permission filter because it is the source used to build the permission-driven navigation tree itself.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the request while the sidebar query is being processed.</param>
    /// <returns>A standardized response containing the hierarchical menu tree visible to the authenticated user.</returns>
    [Authorize]
    [SkipDynamicAuthorization]
    [HttpGet("sidebar")]
    [ProducesResponseType(typeof(Response<IReadOnlyCollection<MenuOptionResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSidebarMenu(CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetSidebarMenuQuery(), cancellationToken);
        return Ok(response);
    }



    /// <summary>
    /// Clears one or more cached entries associated with a specific user and company scope.
    /// The request can target the <c>sidebar</c> cache, the <c>permission</c> cache, or <c>all</c> cache entries for the provided identifiers.
    /// </summary>
    /// <param name="command">The payload containing the user, company, and cache target that should be cleared.</param>
    /// <param name="cancellationToken">Token used to cancel the clean-cache request while the command is being processed.</param>
    /// <returns>A standardized response indicating that the requested cache entries were removed successfully.</returns>
    [Authorize(Roles = "SuperAdmin")]
    [SkipDynamicAuthorization]
    [HttpPost("cleancache")]
    [HttpPost("sidebar/cache/invalidate")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CleanCache(
        [FromBody] CleanCacheCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }



    /// <summary>
    /// Returns every active company linked to the specified user and indicates which assignment is
    /// currently marked as the default operational context. SPEC 28 / F8 restricted this endpoint
    /// to <c>SuperAdmin</c> because the previous behaviour leaked cross-tenant memberships (H2).
    /// </summary>
    /// <param name="userId">The unique identifier of the user whose company assignments should be listed.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the company-assignment query executes.</param>
    /// <returns>A standardized response containing the linked companies for the requested user.</returns>
    [Authorize(Roles = "SuperAdmin")]
    [SkipDynamicAuthorization]
    [HttpGet("{userId:guid}/companies")]
    [ProducesResponseType(typeof(Response<IEnumerable<UserCompanyDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserCompanies(Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetUserCompaniesQuery(userId), cancellationToken);

        if (!response.IsSuccess && string.Equals(response.Message, "User not found.", StringComparison.Ordinal))
        {
            return NotFound(response);
        }

        return Ok(response);
    }



    /// <summary>
    /// Adds (or refreshes the role set of) a user's membership in a company.
    /// SPEC 28 / F3 (item 18, <c>POST /Users/{userId}/companies</c>).
    /// SuperAdmin only — the body's <c>companyId</c> is the tenant of the new membership, not the
    /// caller's tenant. The endpoint is idempotent and reactivates soft-deleted rows in place so
    /// the unique <c>(UserId, CompanyId)</c> index never collides.
    /// </summary>
    /// <param name="userId">User whose membership should be added or refreshed.</param>
    /// <param name="request">Payload containing the target company id and the role ids to assign.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    [Authorize(Roles = "SuperAdmin")]
    [SkipDynamicAuthorization]
    [HttpPost("{userId:guid}/companies")]
    [ProducesResponseType(typeof(Response<AddUserCompanyResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddUserCompany(
        Guid userId,
        [FromBody] AddUserCompanyRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new AddUserCompanyCommand(userId, request.CompanyId, request.RoleIds),
            cancellationToken);

        return MapCompaniesResponse(response);
    }



    /// <summary>
    /// Removes a user's membership in a company and every role assignment attached to that pair.
    /// SPEC 28 / F4 (item 18, <c>DELETE /Users/{userId}/companies/{companyId}</c>).
    /// SuperAdmin only. Guard order from the spec: last-company beats default-company so a
    /// single-tenant user gets the more useful error.
    /// </summary>
    /// <param name="userId">User whose membership should be removed.</param>
    /// <param name="companyId">Company of the membership to remove.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    [Authorize(Roles = "SuperAdmin")]
    [SkipDynamicAuthorization]
    [HttpDelete("{userId:guid}/companies/{companyId:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveUserCompany(
        Guid userId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new RemoveUserCompanyCommand(userId, companyId),
            cancellationToken);

        return MapCompaniesResponse(response);
    }



    /// <summary>
    /// Resolves the effective permissions of a user inside the caller's tenant. SPEC 28 / F5
    /// (item 19, <c>GET /Users/{userId}/effective-permissions</c>). The tenant comes from the JWT
    /// (SPEC 23) — the route deliberately does NOT expose a <c>?companyId=</c> parameter. The
    /// handler runs a fresh Dapper query (no <c>PermissionService</c> cache) so the panel reflects
    /// the live DB state.
    /// </summary>
    /// <param name="userId">User whose effective permissions are being inspected.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    [HttpGet("{userId:guid}/effective-permissions")]
    [ProducesResponseType(typeof(Response<UserEffectivePermissionsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserEffectivePermissions(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new GetUserEffectivePermissionsQuery(userId),
            cancellationToken);

        if (!response.IsSuccess)
        {
            return response.Message switch
            {
                "USER_NOT_FOUND" => NotFound(response),
                "TENANT_REQUIRED" => BadRequest(response),
                _ => BadRequest(response)
            };
        }

        return Ok(response);
    }



    /// <summary>
    /// Sets the default company for a user and clears any previous default assignment in the same operation.
    /// This endpoint is intended for multi-company users that need to change the company context used by subsequent application workflows.
    /// </summary>
    /// <param name="userId">The unique identifier of the user whose default company should be changed.</param>
    /// <param name="companyId">The unique identifier of the company that must become the new default context.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the update command is being executed.</param>
    /// <returns>A standardized response containing the company identifier that is now marked as default.</returns>
    [HttpPut("{userId:guid}/default-company/{companyId:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetDefaultCompany(Guid userId, Guid companyId, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new SetDefaultCompanyCommand(userId, companyId), cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }



    /// <summary>
    /// Returns the active users registered in the system together with all roles currently assigned to each one.
    /// Tenant-scoped by SPEC 28 / F2: only users with an active membership in the caller's tenant.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the request while the user-role query is being processed.</param>
    /// <returns>A standardized response containing the user list and their assigned roles.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<IEnumerable<UserWithRolesDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUsersWithRoles(CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetUsersWithRolesQuery(), cancellationToken);

        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }



    /// <summary>
    /// Bulk add / remove roles across up to <c>200</c> users in a single transaction. SPEC 28 / F6
    /// (item 20, <c>PUT /Users/roles/bulk</c>). Delta semantics: rows not present in either input
    /// list are left untouched. Declared <b>before</b> <see cref="ReplaceUserRoles"/> so the literal
    /// <c>roles/bulk</c> segment wins the routing match over the templated <c>{userId:guid}/roles</c>.
    /// </summary>
    /// <param name="request">Bulk payload with user ids, add-role ids, and remove-role ids.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    [HttpPut("roles/bulk")]
    [ProducesResponseType(typeof(Response<BulkUpdateUserRolesResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BulkUpdateUserRoles(
        [FromBody] BulkUpdateUserRolesRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new BulkUpdateUserRolesCommand(request.UserIds, request.AddRoleIds, request.RemoveRoleIds),
            cancellationToken);

        return response.IsSuccess ? Ok(response) : BadRequest(response);
    }



    /// <summary>
    /// Replaces the full role set assigned to a specific user. SPEC 28 / F2: the underlying handler
    /// now writes to <c>Security.UserRoleCompanies</c> (the table that governs authorization) and
    /// resolves names against the caller's tenant. The external contract — request shape
    /// <c>{ roles: ["Admin"] }</c> and response <c>Response&lt;UserWithRolesDto&gt;</c> — is unchanged.
    /// </summary>
    /// <param name="userId">The unique identifier of the user whose roles should be replaced.</param>
    /// <param name="request">The payload containing the final list of roles that must remain assigned to the user.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the role-replacement command is being handled.</param>
    /// <returns>A standardized response containing the updated user and role assignment projection.</returns>
    [HttpPut("{userId:guid}/roles")]
    [ProducesResponseType(typeof(Response<UserWithRolesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplaceUserRoles(
        Guid userId,
        [FromBody] UpdateUserRolesDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ReplaceUserRolesCommand(userId, request.Roles), cancellationToken);

        if (!response.IsSuccess && string.Equals(response.Message, "User not found.", StringComparison.Ordinal))
        {
            return NotFound(response);
        }

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }



    /// <summary>
    /// Invites a user to the caller's tenant. <c>POST /Users/invite</c> (SPEC 27 item 15).
    /// Tenant context comes from the bearer token, not the body.
    /// </summary>
    /// <param name="request">Invite payload (email + names + role ids).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("invite")]
    [ProducesResponseType(typeof(Response<InviteUserResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Invite(
        [FromBody] InviteUserRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new InviteUserCommand(request.Email, request.FirstName, request.LastName, request.RoleIds),
            cancellationToken);

        return MapInviteResponse(response);
    }



    /// <summary>
    /// Activates or deactivates a user in the caller's tenant. <c>PUT /Users/{userId}/status</c>
    /// (SPEC 27 item 16).
    /// </summary>
    /// <param name="userId">Target user.</param>
    /// <param name="request">Status payload (isActive + reason).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPut("{userId:guid}/status")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeStatus(
        Guid userId,
        [FromBody] ChangeUserStatusRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new ChangeUserStatusCommand(userId, request.IsActive, request.Reason),
            cancellationToken);

        return MapChangeStatusResponse(response);
    }



    /// <summary>
    /// Forces a password reset on a target user. <c>POST /Users/{userId}/force-password-reset</c>
    /// (SPEC 27 item 17). Requires the <c>CanExecute</c> permission flag (not the default
    /// <c>CanCreate</c>) because the operation is administrative, not a resource creation.
    /// </summary>
    /// <param name="userId">Target user.</param>
    /// <param name="request">Force-reset payload (optional reason).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{userId:guid}/force-password-reset")]
    [RequirePermission(PermissionFlags.CanExecute)]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ForcePasswordReset(
        Guid userId,
        [FromBody] ForceUserPasswordResetRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new ForceUserPasswordResetCommand(userId, request.Reason),
            cancellationToken);

        return MapForceResetResponse(response);
    }

    // ──────────────────────────────────────────────
    //  Response → HTTP mapping (SPEC 28 F8 table)
    // ──────────────────────────────────────────────

    private IActionResult MapCompaniesResponse<T>(Response<T> response)
    {
        if (response.IsSuccess)
        {
            return Ok(response);
        }

        return response.Message switch
        {
            "TENANT_REQUIRED" => BadRequest(response),
            "USER_NOT_FOUND" => NotFound(response),
            "COMPANY_NOT_FOUND" => NotFound(response),
            "ROLE_NOT_FOUND" => NotFound(response),
            "MEMBERSHIP_NOT_FOUND" => NotFound(response),
            "CANNOT_REMOVE_DEFAULT_COMPANY" => Conflict(response),
            "CANNOT_REMOVE_LAST_COMPANY" => Conflict(response),
            _ => BadRequest(response)
        };
    }

    private IActionResult MapInviteResponse(Response<InviteUserResultDto> response)
    {
        if (response.IsSuccess)
        {
            return Ok(response);
        }

        return response.Message switch
        {
            "TENANT_REQUIRED" => BadRequest(response),
            "ROLE_NOT_FOUND" => NotFound(response),
            "USER_ALREADY_EXISTS" => Conflict(response),
            "EMAIL_DELIVERY_FAILED" => StatusCode(StatusCodes.Status502BadGateway, response),
            "FRONTEND_URL_NOT_CONFIGURED" => StatusCode(StatusCodes.Status500InternalServerError, response),
            _ => BadRequest(response)
        };
    }

    private IActionResult MapChangeStatusResponse(Response<bool> response)
    {
        if (response.IsSuccess)
        {
            return Ok(response);
        }

        return response.Message switch
        {
            "TENANT_REQUIRED" => BadRequest(response),
            "USER_NOT_FOUND" => NotFound(response),
            "CANNOT_CHANGE_OWN_STATUS" => Conflict(response),
            "STATUS_UNCHANGED" => Conflict(response),
            _ => BadRequest(response)
        };
    }

    private IActionResult MapForceResetResponse(Response<bool> response)
    {
        if (response.IsSuccess)
        {
            return Ok(response);
        }

        return response.Message switch
        {
            "TENANT_REQUIRED" => BadRequest(response),
            "USER_INACTIVE" => BadRequest(response),
            "USER_NOT_FOUND" => NotFound(response),
            "EMAIL_DELIVERY_FAILED" => StatusCode(StatusCodes.Status502BadGateway, response),
            "FRONTEND_URL_NOT_CONFIGURED" => StatusCode(StatusCodes.Status500InternalServerError, response),
            _ => BadRequest(response)
        };
    }

}