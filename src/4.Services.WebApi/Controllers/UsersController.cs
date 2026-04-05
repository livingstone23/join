using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Refresh;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Application.UseCases.Security.Queries.GetMyCompanyUserReport;
using JOIN.Application.UseCases.Security.Queries.GetSystemWideUserReport;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Services.WebApi.Controllers;

/// <summary>
/// API endpoints for user and role assignments.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly IMediator _mediator;

    public UsersController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ApplicationDbContext dbContext,
        IMediator mediator)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <summary>
    /// Authenticates a user and returns the JWT session payload.
    /// </summary>
    /// <param name="command">The login request payload.</param>
    /// <param name="cancellationToken">The cancellation token for the operation.</param>
    /// <returns>The authenticated session payload.</returns>
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
    /// Registers a new active user without company assignments.
    /// </summary>
    /// <param name="command">The registration request payload.</param>
    /// <param name="cancellationToken">The cancellation token for the operation.</param>
    /// <returns>A standardized response containing the created user identifier.</returns>
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
    /// Rotates a valid refresh token and returns a renewed authenticated session.
    /// </summary>
    /// <param name="command">The refresh token payload.</param>
    /// <param name="cancellationToken">The cancellation token for the operation.</param>
    /// <returns>The renewed authenticated session payload.</returns>
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
    /// Logs the current user out of the active session.
    /// </summary>
    /// <returns>A success response indicating the session has been closed.</returns>
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
    /// Returns the user management and activity report across all companies.
    /// </summary>
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
    /// Returns the user management and activity report scoped to the caller's active company.
    /// </summary>
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
    /// Returns active users with all assigned roles.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<IEnumerable<UserWithRolesDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsersWithRoles()
    {
        var users = await _dbContext.Users
            .Where(u => u.GcRecord == 0)
            .OrderBy(u => u.UserName)
            .ToListAsync();

        var userRoles = await (from ur in _dbContext.UserRoles
                               join r in _dbContext.Roles on ur.RoleId equals r.Id
                               select new { ur.UserId, RoleName = r.Name ?? string.Empty })
            .ToListAsync();

        var rolesByUser = userRoles
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyCollection<string>)g.Select(x => x.RoleName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name)
                    .ToArray());

        var result = users.Select(user => new UserWithRolesDto
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            IsActive = user.IsActive,
            Roles = rolesByUser.TryGetValue(user.Id, out var roles) ? roles : Array.Empty<string>()
        });

        return Ok(new Response<IEnumerable<UserWithRolesDto>>
        {
            Data = result,
            IsSuccess = true,
            Message = "Users with roles retrieved successfully."
        });
    }

    /// <summary>
    /// Replaces all roles assigned to one user.
    /// </summary>
    [HttpPut("{userId:guid}/roles")]
    [ProducesResponseType(typeof(Response<UserWithRolesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<UserWithRolesDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<UserWithRolesDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplaceUserRoles(Guid userId, [FromBody] UpdateUserRolesDto request)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return NotFound(new Response<UserWithRolesDto>
            {
                IsSuccess = false,
                Message = "User not found."
            });
        }

        var requestedRoles = request.Roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var normalizedRequestedRoles = requestedRoles
            .Select(role => role.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingRoles = await _roleManager.Roles
            .Where(role => role.Name != null && normalizedRequestedRoles.Contains(role.NormalizedName!))
            .Select(role => role.Name!)
            .ToListAsync();

        var missingRoles = requestedRoles
            .Except(existingRoles, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingRoles.Length > 0)
        {
            return BadRequest(new Response<UserWithRolesDto>
            {
                IsSuccess = false,
                Message = "One or more roles do not exist.",
                Errors = missingRoles.Select(role => $"Role '{role}' does not exist.")
            });
        }

        var currentRoles = await _userManager.GetRolesAsync(user);

        var rolesToAdd = existingRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        if (rolesToAdd.Length > 0)
        {
            var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                return BadRequest(new Response<UserWithRolesDto>
                {
                    IsSuccess = false,
                    Message = "Unable to add one or more roles.",
                    Errors = addResult.Errors.Select(error => error.Description)
                });
            }
        }

        var rolesToRemove = currentRoles.Except(existingRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        if (rolesToRemove.Length > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                return BadRequest(new Response<UserWithRolesDto>
                {
                    IsSuccess = false,
                    Message = "Unable to remove one or more roles.",
                    Errors = removeResult.Errors.Select(error => error.Description)
                });
            }
        }

        var updatedRoles = await _userManager.GetRolesAsync(user);

        return Ok(new Response<UserWithRolesDto>
        {
            Data = new UserWithRolesDto
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                IsActive = user.IsActive,
                Roles = updatedRoles.OrderBy(role => role).ToArray()
            },
            IsSuccess = true,
            Message = "User roles updated successfully."
        });
    }
}
