
using JOIN.Application.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;



namespace JOIN.Services.WebApi.Filters;



/// <summary>
/// Global authorization filter that intercepts HTTP requests and validates user permissions
/// dynamically based on the requested controller and HTTP method.
/// </summary>
public class DynamicAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly IPermissionService _permissionService;

    // Constante para evitar "magic strings" y errores tipográficos
    private const string SuperAdminRoleName = "SuperAdmin"; 

    public DynamicAuthorizationFilter(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // 1. Bypass authorization for endpoints decorated with [AllowAnonymous] (e.g., Login).
        if (context.ActionDescriptor.EndpointMetadata.Any(em => em.GetType() == typeof(AllowAnonymousAttribute)))
            return;

        // 2. Ensure the request is mapped to a Controller action.
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor) return;
        
        // Extract the target controller and the HTTP method (GET, POST, PUT, DELETE).
        string controllerName = descriptor.ControllerName; 
        string httpMethod = context.HttpContext.Request.Method; 

        // 3. Extract user identity, ROLES, and tenant context from the JWT Claims.
        var user = context.HttpContext.User;
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
        var companyId = user.FindFirst("CompanyId")?.Value;

        // Block access if the UserId is completely missing (Invalid Token).
        if (string.IsNullOrEmpty(userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // --- SUPERADMIN BYPASS ---
        // If the user is SuperAdmin, grant full access immediately and ignore CompanyId.
        if (roles.Any(role => string.Equals(role, SuperAdminRoleName, StringComparison.OrdinalIgnoreCase)))
        {
            return; // Successful authorization, stop filter execution.
        }

        // --- REGULAR USERS ---
        // Block access immediately if a regular user is missing the tenant context (CompanyId).
        if (string.IsNullOrEmpty(companyId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // 4. Validate permissions against the cache or database via the Application layer service.
        bool hasAccess = await _permissionService.HasPermissionAsync(userId, companyId, controllerName, httpMethod);

        // If the user lacks the required permission, return a 403 Forbidden response.
        if (!hasAccess)
        {
            context.Result = new ForbidResult(); 
        }
    }
}
