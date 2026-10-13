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

    /// <summary>
    /// Resolves whether the module(s) that own a permission resource are enabled for the company.
    /// A resource belongs to the modules of the <c>SystemOptions</c> whose <c>ControllerName</c> matches it
    /// (same normalization as <see cref="HasPermissionAsync(string, string, string, string)"/>). It is enabled
    /// if ANY of those modules is active globally (<c>SystemModule.IsActive</c>) and for the company
    /// (<c>CompanyModule.IsActive</c>, not soft-deleted). A resource with no <c>SystemOption</c> is not
    /// module-scoped and returns <see cref="ModuleAccessResult.IsEnabled"/> = <c>true</c>.
    /// Introduced by SPEC 48 to gate every controller on the company's active modules before the
    /// per-role permission check runs.
    /// </summary>
    /// <param name="companyId">Active company tenant.</param>
    /// <param name="resourceName">Controller or permission resource being requested.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ModuleAccessResult> GetModuleAccessAsync(Guid companyId, string resourceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the per-company module cache (<c>company-modules:v1:{companyId}</c>) so the next
    /// <see cref="GetModuleAccessAsync"/> call rebuilds it from the database. Used after
    /// mutating <c>CompanyModule</c> rows so module assignment changes are reflected immediately.
    /// </summary>
    /// <remarks>
    /// Introduced by SPEC 48 to invalidate the cache for a single tenant after a module is
    /// activated or deactivated for that company.
    /// </remarks>
    Task InvalidateCompanyModuleCacheAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the global version token shared by every <c>company-modules:v1:{companyId}</c> entry.
    /// Used after mutating <c>SystemModule</c> or the <c>ModuleId</c>/<c>ControllerName</c> of a
    /// <c>SystemOption</c> — changes that affect every company, so we cannot enumerate them.
    /// </summary>
    /// <remarks>
    /// <para>Introduced by SPEC 48: <c>IMemoryCache</c> cannot remove keys by prefix, so the
    /// service exposes a single <see cref="CancellationTokenSource"/> and ties each cached entry to
    /// its token via <c>MemoryCacheEntryOptions.AddExpirationToken</c>.</para>
    /// <para>The next <see cref="GetModuleAccessAsync"/> call rebuilds the per-company snapshot.</para>
    /// </remarks>
    Task InvalidateModuleCacheForAllCompaniesAsync(CancellationToken cancellationToken = default);
}
