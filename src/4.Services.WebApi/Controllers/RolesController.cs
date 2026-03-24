using JOIN.Application.Common;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Services.WebApi.Controllers;

/// <summary>
/// API endpoints for role catalog queries.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class RolesController : ControllerBase
{
    private readonly RoleManager<ApplicationRole> _roleManager;

    public RolesController(RoleManager<ApplicationRole> roleManager)
    {
        _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
    }

    /// <summary>
    /// Returns available role names sorted alphabetically.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<IEnumerable<string>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var roles = await _roleManager.Roles
            .OrderBy(role => role.Name)
            .Select(role => role.Name ?? string.Empty)
            .Where(roleName => roleName != string.Empty)
            .ToListAsync();

        return Ok(new Response<IEnumerable<string>>
        {
            Data = roles,
            IsSuccess = true,
            Message = "Roles retrieved successfully."
        });
    }
}
