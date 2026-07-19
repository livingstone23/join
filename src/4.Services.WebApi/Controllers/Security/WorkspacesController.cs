using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Workspaces;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Workspaces.Commands.SwitchCompany;
using JOIN.Application.UseCases.Security.Workspaces.Queries.GetMyCompanies;
using JOIN.Application.UseCases.Security.Workspaces.Queries.GetMyRolesByCompany;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;



namespace JOIN.Services.WebApi.Controllers.Security;



/// <summary>
/// Exposes authenticated multi-tenant workspace endpoints for resolving companies, roles, and company-context switching.
/// The controller keeps an HTTP-only role and delegates processing to MediatR handlers in the Application layer.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/workspaces")]
[Produces("application/json")]
[Authorize]
[PermissionResource("Users")]
public class WorkspacesController(ISender sender, ICurrentUserService currentUserService) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

    /// <summary>
    /// Returns the companies assigned to the authenticated user.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>
    /// Returns <c>200</c> with the user-company list, <c>400</c> for invalid request state,
    /// <c>401</c> when the caller is not authenticated, or <c>404</c> when no company assignments are found.
    /// </returns>
    [HttpGet("my-companies")]
    [ProducesResponseType(typeof(Response<IReadOnlyCollection<MyCompanyItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyCompanies(CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(Response<object>.Error("AUTHENTICATED_USER_REQUIRED"));
        }

        var response = await _sender.Send(new GetMyCompaniesQuery(userId), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Returns the roles assigned to the authenticated user in a specific company context.
    /// </summary>
    /// <param name="companyId">The company identifier used to scope the role lookup.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>
    /// Returns <c>200</c> with the role list, <c>400</c> for invalid request state,
    /// <c>401</c> when the caller is not authenticated, or <c>404</c> when the company assignment does not exist.
    /// </returns>
    [HttpGet("{companyId:guid}/my-roles")]
    [ProducesResponseType(typeof(Response<IReadOnlyCollection<MyCompanyRoleDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyRoles(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(Response<object>.Error("AUTHENTICATED_USER_REQUIRED"));
        }

        var response = await _sender.Send(new GetMyRolesByCompanyQuery(userId, companyId), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Switches the authenticated user's active company context and requests a renewed JWT session payload.
    /// </summary>
    /// <param name="request">The payload containing the target company identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the command is being processed.</param>
    /// <returns>
    /// Returns <c>200</c> with a renewed token payload, <c>400</c> for invalid request state,
    /// <c>401</c> when the caller is not authenticated, or <c>404</c> when the company assignment does not exist.
    /// </returns>
    [HttpPost("switch-company")]
    [ProducesResponseType(typeof(Response<SwitchCompanyResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SwitchCompany([FromBody] SwitchCompanyRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(Response<object>.Error("AUTHENTICATED_USER_REQUIRED"));
        }

        var response = await _sender.Send(new SwitchCompanyCommand(userId, request.CompanyId), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Tries to resolve the current authenticated user identifier from JWT claims.
    /// </summary>
    /// <param name="userId">When this method returns, contains the parsed user identifier if available.</param>
    /// <returns><see langword="true"/> when a valid user identifier is present; otherwise <see langword="false"/>.</returns>
    private bool TryGetCurrentUserId(out Guid userId)
    {
        return Guid.TryParse(_currentUserService.UserId, out userId);
    }
    
}