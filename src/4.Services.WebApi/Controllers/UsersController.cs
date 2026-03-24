using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Domain.Security;
using JOIN.Infrastructure.Contexts;
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

    public UsersController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ApplicationDbContext dbContext)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
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
