


namespace JOIN.Application.Interface;



/// <summary>
/// Defines the contract for the permission evaluation service.
/// Acts as the core authorization brain of the application, responsible for resolving 
/// user access rights based on their assigned roles and the current tenant (Company) context.
/// Designed to be completely decoupled from the presentation layer (e.g., HTTP, MVC).
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Validates if the user has permission to execute an action on a specific resource.
    /// Uses in-memory caching to ensure sub-millisecond response times, avoiding direct DB hits.
    /// </summary>
    /// <param name="userId">The ID of the user requesting access.</param>
    /// <param name="companyId">The ID of the tenant (Company) the user is currently operating under.</param>
    /// <param name="resourceName">The name of the resource or controller (e.g., "Countries").</param>
    /// <param name="actionType">The requested action type or HTTP method (e.g., "GET", "POST").</param>
    /// <returns>True if the user has access; otherwise, false.</returns>
    Task<bool> HasPermissionAsync(string userId, string companyId, string resourceName, string actionType);

}
