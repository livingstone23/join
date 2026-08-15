using JOIN.Domain.Security;

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

    /// <summary>
    /// Validates if the user has permission to execute an action on a specific resource,
    /// honoring an explicit flag override (e.g. <see cref="PermissionFlags.CanExport"/> on a GET endpoint).
    /// </summary>
    /// <param name="userId">The ID of the user requesting access.</param>
    /// <param name="companyId">The ID of the tenant (Company) the user is currently operating under.</param>
    /// <param name="resourceName">The name of the resource or controller (e.g., "Countries").</param>
    /// <param name="actionType">The requested action type or HTTP method (e.g., "GET", "POST").</param>
    /// <param name="explicitFlag">When provided, evaluates this specific flag instead of the HTTP-verb default mapping.</param>
    /// <returns>True if the user has access; otherwise, false.</returns>
    Task<bool> HasPermissionAsync(string userId, string companyId, string resourceName, string actionType, PermissionFlags? explicitFlag);

    /// <summary>
    /// Drops both the permission snapshot cache (<c>permissions:v2:{companyId}:{userId}</c>)
    /// and the sidebar menu cache (<c>sidebar:{companyId}:{userId}</c>) for the given
    /// user in the given tenant. Used after mutating <c>RoleSystemOption</c> rows so the
    /// next <see cref="HasPermissionAsync"/> call rebuilds the snapshot from scratch.
    /// </summary>
    /// <remarks>
    /// Introduced by SPEC 25 because the legacy <c>InvalidateSidebarCacheCommand</c> writes
    /// the wrong key for the permissions snapshot (it removes <c>permissions:{companyId}:{userId}</c>,
    /// not the real <c>permissions:v2:{companyId}:{userId}</c>). A future spec will fix that command;
    /// in the meantime, callers that need a guaranteed snapshot refresh should invoke this method.
    /// </remarks>
    Task InvalidateUserCacheAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default);
}
