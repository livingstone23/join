using JOIN.Application.Common;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;



namespace JOIN.Services.WebApi.Controllers.Security;



/// <summary>
/// Exposes read-only endpoints for the role catalog used by the security and administration modules.
/// The controller returns the available Identity role names so clients can populate assignment and permission-management screens.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[PermissionResource("Roles")]
public class RolesController(RoleManager<ApplicationRole> roleManager) : ControllerBase
{
    private readonly RoleManager<ApplicationRole> _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));

    /// <summary>
    /// Returns the active role names registered in the Identity store, sorted alphabetically for predictable UI rendering.
    /// This endpoint is typically used to populate role selectors when assigning or replacing user permissions.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the request while the role list is being materialized.</param>
    /// <returns>A standardized response containing the role names available to the application.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<IEnumerable<string>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
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
}
