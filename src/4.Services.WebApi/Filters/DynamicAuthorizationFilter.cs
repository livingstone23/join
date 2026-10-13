using JOIN.Application.Common.Settings;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace JOIN.Services.WebApi.Filters;

/// <summary>
/// Global authorization filter that intercepts HTTP requests and validates user permissions
/// dynamically based on the requested controller, HTTP method, and any explicit flag overrides.
/// SPEC 48 adds a module-level check (before the per-role permission check) so non-SuperAdmin
/// users cannot call APIs of modules that are not active for their company.
/// </summary>
public class DynamicAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly IPermissionService _permissionService;
    private readonly IOptions<ModuleEnforcementSettings> _moduleSettings;

    // Constant role name for SuperAdmin users who bypass all permission checks.
    // This is a simple string comparison, so it should be kept in sync with the actual role name used in the system.
    private const string SuperAdminRoleName = "SuperAdmin";

    // Suffix stripped from the controller class name when inferring the resource name
    // (e.g. "PersonsController" → "Persons") to match the seed `SystemOption.ControllerName`.
    private const string ControllerSuffix = "Controller";

    public DynamicAuthorizationFilter(
        IPermissionService permissionService,
        IOptions<ModuleEnforcementSettings> moduleSettings)
    {
        _permissionService = permissionService;
        _moduleSettings = moduleSettings;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // 1. Bypass dynamic authorization for endpoints explicitly marked to skip it
        // or for [AllowAnonymous] endpoints such as login/refresh.
        if (context.ActionDescriptor.EndpointMetadata.Any(em =>
                em is AllowAnonymousAttribute || em is SkipDynamicAuthorizationAttribute))
        {
            return;
        }

        // 2. Ensure the request is mapped to a Controller action.
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor) return;

        // Resolve the explicit permission resource, then fall back to the controller name.
        var permissionResource = ResolvePermissionResourceName(descriptor);
        if (string.IsNullOrWhiteSpace(permissionResource))
        {
            // Fail-closed: a controller with no inferable resource name is a bug, not an open door.
            context.Result = new ForbidResult();
            return;
        }

        // Read the explicit flag override (e.g. [RequirePermission(CanExport)] on a GET endpoint).
        var explicitFlag = context.ActionDescriptor.EndpointMetadata
            .OfType<RequirePermissionAttribute>()
            .FirstOrDefault()?.Flag;

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
        if (string.IsNullOrEmpty(companyId) || !Guid.TryParse(companyId, out var parsedCompanyId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // 4. SPEC 48 — module-level gate (B1/B2/B4). Runs BEFORE the permission check so the user always
        // sees the same error regardless of role, and so an unassigned module is unreachable. The switch
        // defaults to false; deployments enable it after reviewing GET /CompanyModules/diagnostics.
        if (_moduleSettings.Value.EnforceCompanyModules)
        {
            var access = await _permissionService.GetModuleAccessAsync(parsedCompanyId, permissionResource, context.HttpContext.RequestAborted);
            if (!access.IsEnabled)
            {
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Module not enabled",
                    Detail = access.ModuleName is null
                        ? "The company does not have any of the modules required for this resource enabled."
                        : $"The company does not have the module '{access.ModuleName}' enabled.",
                    Extensions =
                    {
                        ["code"] = "MODULE_NOT_ENABLED",
                        ["module"] = access.ModuleName
                    }
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return;
            }
        }

        // 5. Validate permissions against the cache or database via the Application layer service.
        bool hasAccess = await _permissionService.HasPermissionAsync(
            userId, companyId, permissionResource, httpMethod, explicitFlag);

        // If the user lacks the required permission, return a 403 Forbidden response.
        if (!hasAccess)
        {
            context.Result = new ForbidResult();
        }
    }

    /// <summary>
    /// Resolves the permission resource name for the current action.
    /// Precedence: action-level attribute → class-level attribute → controller class name (suffix stripped) → null.
    /// </summary>
    /// <param name="descriptor">The MVC action descriptor for the current request.</param>
    /// <returns>The resolved resource name, or <c>null</c> when nothing can be inferred.</returns>
    private static string? ResolvePermissionResourceName(ControllerActionDescriptor descriptor)
    {
        var actionResource = descriptor.MethodInfo
            .GetCustomAttributes(inherit: true)
            .OfType<PermissionResourceAttribute>()
            .FirstOrDefault()?.ResourceName;

        if (!string.IsNullOrWhiteSpace(actionResource))
        {
            return actionResource;
        }

        var controllerResource = descriptor.ControllerTypeInfo
            .GetCustomAttributes(inherit: true)
            .OfType<PermissionResourceAttribute>()
            .FirstOrDefault()?.ResourceName;

        if (!string.IsNullOrWhiteSpace(controllerResource))
        {
            return controllerResource;
        }

        // Fallback: derive from the controller class name, stripping the "Controller" suffix
        // to match the seed `SystemOption.ControllerName` (e.g. "PersonsController" → "Persons").
        var controllerName = descriptor.ControllerName;
        if (!string.IsNullOrWhiteSpace(controllerName) &&
            controllerName.EndsWith(ControllerSuffix, StringComparison.OrdinalIgnoreCase))
        {
            controllerName = controllerName[..^ControllerSuffix.Length];
        }

        return string.IsNullOrWhiteSpace(controllerName) ? null : controllerName;
    }
}