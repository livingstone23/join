using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Refresh;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Application.UseCases.Security.Queries.GetMyCompanyUserReport;
using JOIN.Application.UseCases.Security.Queries.GetSidebarMenu;
using JOIN.Application.UseCases.Security.Queries.GetSystemWideUserReport;
using JOIN.Application.UseCases.Security.UserCompanies.Commands.SetDefaultCompany;
using JOIN.Application.UseCases.Security.UserCompanies.Queries.GetUserCompanies;
using JOIN.Application.UseCases.Security.Users.Commands.InvalidateSidebarCache;
using JOIN.Application.UseCases.Security.Users.Commands.ReplaceUserRoles;
using JOIN.Application.UseCases.Security.Users.Queries.GetUsersWithRoles;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;



namespace JOIN.Services.WebApi.Controllers;



/// <summary>
/// Exposes authentication, user-administration, reporting, and user-to-company assignment endpoints for the security module.
/// The controller keeps a thin HTTP-facing role and delegates business logic and data access to the Application layer through MediatR.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
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
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }



    /// <summary>
    /// Registers a new active user account without initial company assignments.
    /// The actual validation and persistence rules are executed in the Application layer through the registration command handler.
    /// </summary>
    /// <param name="command">The registration payload containing the new user data.</param>
    /// <param name="cancellationToken">Token used to cancel the registration request while the command is being handled.</param>
    /// <returns>A standardized response containing the identifier of the newly created user.</returns>
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
    /// <param name="cancellationToken">Token used to cancel the refresh request while the command is being processed.</param>
    /// <returns>The renewed authenticated session payload when the refresh token is valid.</returns>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
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
    /// Access is restricted to `SuperAdmin` users because the response spans multiple tenants and may include cross-company activity information.
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
    /// Returns the user management and activity report restricted to the authenticated user's active company context.
    /// The effective tenant is resolved first from <c>Security.UserCompanies</c> using the default company, then from <c>Security.UserRoleCompanies</c> when necessary, and finally applied to scope the report data safely.
    /// </summary>
    /// <param name="fromDate">Optional lower UTC date boundary used to filter the report window.</param>
    /// <param name="toDate">Optional upper UTC date boundary used to filter the report window.</param>
    /// <param name="roleNames">Optional list of role names used to narrow the report result set.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the company-scoped report query runs.</param>
    /// <returns>A standardized response containing the company-scoped user management report rows.</returns>
    [HttpGet("reports/my-company")]
    [ProducesResponseType(typeof(Response<IReadOnlyCollection<UserManagementReportDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyCompanyReport(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string[]? roleNames = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetMyCompanyUserReportQuery(fromDate, toDate, roleNames),
            cancellationToken);

        return Ok(response);
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
    /// Invalidates the cached sidebar entry associated with a specific user and company scope.
    /// The next call to the sidebar endpoint for the same identifiers will reload the data from the database and repopulate the cache with fresh values.
    /// </summary>
    /// <param name="command">The payload containing the user and company identifiers whose sidebar cache should be cleared.</param>
    /// <param name="cancellationToken">Token used to cancel the invalidation request while the command is being processed.</param>
    /// <returns>A standardized response indicating that the sidebar cache entry was removed successfully.</returns>
    [Authorize(Roles = "SuperAdmin")]
    [SkipDynamicAuthorization]
    [HttpPost("sidebar/cache/invalidate")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> InvalidateSidebarCache(
        [FromBody] InvalidateSidebarCacheCommand command,
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
    /// Returns every active company linked to the specified user and indicates which assignment is currently marked as the default operational context.
    /// This endpoint is used to drive user-context switching screens and related security administration workflows.
    /// </summary>
    /// <param name="userId">The unique identifier of the user whose company assignments should be listed.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the company-assignment query executes.</param>
    /// <returns>A standardized response containing the linked companies for the requested user.</returns>
    [HttpGet("{userId:guid}/companies")]
    [ProducesResponseType(typeof(Response<IEnumerable<UserCompanyDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
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
    /// This endpoint is especially useful for user-administration screens that need a consolidated role view before applying changes.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the request while the user-role query is being processed.</param>
    /// <returns>A standardized response containing the user list and their assigned roles.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<IEnumerable<UserWithRolesDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUsersWithRoles(CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetUsersWithRolesQuery(), cancellationToken);
        return Ok(response);
    }



    /// <summary>
    /// Replaces the full role set assigned to a specific user.
    /// The endpoint receives the desired final role list, delegates the replacement logic to the Application layer, and returns `404`, `400`, or `200` depending on the execution outcome.
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

}
