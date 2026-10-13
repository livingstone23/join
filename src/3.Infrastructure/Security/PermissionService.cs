using Dapper;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace JOIN.Infrastructure.Security;

/// <summary>
/// Evaluates dynamic controller permissions for the current user and company context.
/// </summary>
/// <param name="dbContext">Database context used to load effective role permissions.</param>
/// <param name="memoryCache">Cache used to reduce repetitive permission queries.</param>
/// <param name="connectionFactory">Read-only connection factory used to query the module mapping (SPEC 48).</param>
public class PermissionService(
    ApplicationDbContext dbContext,
    IMemoryCache memoryCache,
    ISqlConnectionFactory connectionFactory) : IPermissionService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory;

    /// <summary>
    /// Cache key version prefix. Bumped to <c>v2</c> in SPEC 17 because the cached value
    /// shape changed from 4 booleans to 7 (added CanDownload/CanExport/CanExecute).
    /// Existing caches with the previous key are ignored and re-built on first call.
    /// </summary>
    private const string CacheKeyPrefix = "permissions:v2";

    /// <summary>
    /// Cache key prefix for the per-company module snapshot (SPEC 48). The cached value is
    /// <c>Dictionary&lt;string, ModuleAccessResult&gt;</c> keyed by the normalized resource name
    /// (same normalization as <see cref="HasPermissionAsync"/>), where each entry tells whether
    /// any owning module is enabled for the company, plus that module's name for error messages.
    /// </summary>
    private const string ModuleCacheKeyPrefix = "company-modules:v1";

    /// <summary>
    /// Global version token (SPEC 48, E): every <c>company-modules:v1:{companyId}</c> entry is created
    /// with <see cref="MemoryCacheEntryOptions.AddExpirationToken(MemoryCacheEntryOptions, System.Threading.CancellationToken)"/>
    /// wired to this CTS. Cancelling it invalidates every per-company entry at once, which is what we need
    /// when <see cref="Domain.Admin.SystemModule"/> rows change (because <see cref="IMemoryCache"/> cannot
    /// remove keys by prefix).
    /// Static on purpose: <see cref="PermissionService"/> is registered as scoped, so an instance field
    /// would only live for one request and the cancellation would never reach entries created by other
    /// scopes. The <see cref="IMemoryCache"/> it guards is a process-wide singleton, and so is this token.
    /// </summary>
    private static CancellationTokenSource _moduleVersionCts = new();

    /// <summary>
    /// Validates whether the current user has permission to execute the requested action,
    /// using the HTTP verb default mapping (GET→CanRead, POST→CanCreate, PUT/PATCH→CanUpdate, DELETE→CanDelete).
    /// </summary>
    public Task<bool> HasPermissionAsync(string userId, string companyId, string resourceName, string actionType)
        => HasPermissionAsync(userId, companyId, resourceName, actionType, explicitFlag: null);

    /// <summary>
    /// Validates whether the current user has permission to execute the requested action.
    /// When <paramref name="explicitFlag"/> is provided, evaluates that flag directly;
    /// otherwise falls back to the HTTP-verb default mapping.
    /// </summary>
    /// <param name="userId">The authenticated user identifier.</param>
    /// <param name="companyId">The active company identifier.</param>
    /// <param name="resourceName">The controller or resource name being requested.</param>
    /// <param name="actionType">The HTTP method associated with the request.</param>
    /// <param name="explicitFlag">Optional explicit flag override (e.g. <see cref="PermissionFlags.CanExport"/> on GET).</param>
    /// <returns><c>true</c> when access is granted; otherwise, <c>false</c>.</returns>
    public async Task<bool> HasPermissionAsync(string userId, string companyId, string resourceName, string actionType, PermissionFlags? explicitFlag)
    {
        if (!Guid.TryParse(userId, out var parsedUserId) || !Guid.TryParse(companyId, out var parsedCompanyId))
        {
            return false;
        }

        var cacheKey = $"{CacheKeyPrefix}:{companyId}:{userId}";
        var permissions = await _memoryCache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            entry.SlidingExpiration = TimeSpan.FromMinutes(10);

            var roleIds = await _dbContext.UserRoleCompanies
                .AsNoTracking()
                .Where(link => link.UserId == parsedUserId && link.CompanyId == parsedCompanyId && link.GcRecord == 0)
                .Select(link => link.RoleId)
                .Distinct()
                .ToArrayAsync();

            if (roleIds.Length == 0)
            {
                return new Dictionary<string, PermissionFlagsSnapshot>(StringComparer.OrdinalIgnoreCase);
            }

            var rows = await (from roleOption in _dbContext.RoleSystemOptions.AsNoTracking()
                              join systemOption in _dbContext.SystemOptions.AsNoTracking()
                                  on roleOption.SystemOptionId equals systemOption.Id
                              where roleIds.Contains(roleOption.RoleId)
                                    && roleOption.CompanyId == parsedCompanyId
                                    && roleOption.GcRecord == 0
                                    && systemOption.GcRecord == 0
                              select new
                              {
                                  ControllerName = systemOption.ControllerName ?? string.Empty,
                                  roleOption.CanRead,
                                  roleOption.CanCreate,
                                  roleOption.CanUpdate,
                                  roleOption.CanDelete,
                                  roleOption.CanDownload,
                                  roleOption.CanExport,
                                  roleOption.CanExecute
                              })
                .ToListAsync();

            var map = new Dictionary<string, PermissionFlagsSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var normalizedNames = NormalizeControllerNames(row.ControllerName);
                foreach (var name in normalizedNames)
                {
                    map.TryGetValue(name, out var current);
                    map[name] = new PermissionFlagsSnapshot(
                        current.CanRead     || row.CanRead,
                        current.CanCreate   || row.CanCreate,
                        current.CanUpdate   || row.CanUpdate,
                        current.CanDelete   || row.CanDelete,
                        current.CanDownload || row.CanDownload,
                        current.CanExport   || row.CanExport,
                        current.CanExecute  || row.CanExecute);
                }
            }

            return map;
        });

        var normalizedResourceNames = NormalizeControllerNames(resourceName);
        foreach (var name in normalizedResourceNames)
        {
            if (!permissions!.TryGetValue(name, out var snapshot))
            {
                continue;
            }

            if (explicitFlag is { } requested)
            {
                return EvaluateFlag(snapshot, requested);
            }

            return actionType.ToUpperInvariant() switch
            {
                "GET" or "HEAD"  => snapshot.CanRead,
                "POST"           => snapshot.CanCreate,
                "PUT" or "PATCH" => snapshot.CanUpdate,
                "DELETE"         => snapshot.CanDelete,
                _                => false
            };
        }

        return false;
    }

    /// <inheritdoc />
    public Task InvalidateUserCacheAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default)
    {
        // The keys MUST match what HasPermissionAsync and GetSidebarMenuQueryHandler write:
        //   - permissions:v2:{companyId}:{userId}  (this service, snapshot of 7 flags)
        //   - sidebar:{companyId}:{userId}         (sidebar menu query, no version prefix)
        // Remove is a no-op when the key is absent — safe to call without a prior HasPermissionAsync.
        _memoryCache.Remove($"{CacheKeyPrefix}:{companyId}:{userId}");
        _memoryCache.Remove($"sidebar:{companyId}:{userId}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// SPEC 48 B: per-company module snapshot. The query joins <c>Security.SystemOptions</c> to its
    /// <c>Admin.SystemModules</c> and <c>LEFT JOIN</c>s <c>Admin.CompanyModules</c> for the company, so a
    /// resource with no module-scoped <c>SystemOption</c> row simply does not appear in the result and
    /// is treated as "not module-scoped" (enabled). The cache expires absolutely every 30 minutes and
    /// slides every 10; it also listens to the global version token (<see cref="InvalidateModuleCacheForAllCompaniesAsync"/>).
    /// </remarks>
    public async Task<ModuleAccessResult> GetModuleAccessAsync(Guid companyId, string resourceName, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty || string.IsNullOrWhiteSpace(resourceName))
        {
            // Defensive: an empty tenant or resource name is not module-scoped, mirror the SQL behavior.
            return new ModuleAccessResult(IsEnabled: true, ModuleName: null);
        }

        var cacheKey = $"{ModuleCacheKeyPrefix}:{companyId}";
        var snapshot = await _memoryCache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            entry.SlidingExpiration = TimeSpan.FromMinutes(10);
            entry.AddExpirationToken(new CancellationChangeToken(_moduleVersionCts.Token));

            using var connection = _connectionFactory.CreateConnection();
            var rows = (await connection.QueryAsync<ModuleAccessSqlRow>(
                new CommandDefinition(ModuleAccessQuerySql, new { CompanyId = companyId }, cancellationToken: cancellationToken)))
                .AsList();

            // Each row is one (resource, module) pair. Many modules can own the same resource, so we keep
            // every name so callers can check "any module enabled" against the full set; the first module
            // name is what the filter surfaces in the MODULE_NOT_ENABLED message.
            var map = new Dictionary<string, ModuleAccessResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var normalizedNames = NormalizeControllerNames(row.ControllerName);
                foreach (var name in normalizedNames)
                {
                    if (row.IsEnabled)
                    {
                        // Once any owning module is enabled for the company, the resource is enabled and
                        // its first module name becomes the one the user sees in error messages.
                        map[name] = new ModuleAccessResult(IsEnabled: true, ModuleName: row.ModuleName);
                        continue;
                    }

                    // Otherwise keep the first module name we saw for that resource so the message is stable.
                    if (!map.ContainsKey(name))
                    {
                        map[name] = new ModuleAccessResult(IsEnabled: false, ModuleName: row.ModuleName);
                    }
                }
            }

            return map;
        }) ?? new Dictionary<string, ModuleAccessResult>(StringComparer.OrdinalIgnoreCase);

        var normalizedResources = NormalizeControllerNames(resourceName);
        foreach (var name in normalizedResources)
        {
            if (snapshot.TryGetValue(name, out var entry))
            {
                return entry;
            }
        }

        // A resource without a SystemOption is not module-scoped; permission check still applies, but the
        // module gate lets the request through so HasPermissionAsync can decide.
        return new ModuleAccessResult(IsEnabled: true, ModuleName: null);
    }

    /// <inheritdoc />
    public Task InvalidateCompanyModuleCacheAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        _memoryCache.Remove($"{ModuleCacheKeyPrefix}:{companyId}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InvalidateModuleCacheForAllCompaniesAsync(CancellationToken cancellationToken = default)
    {
        // IMemoryCache cannot remove keys by prefix; cancel the shared token so every entry tied to it expires.
        // The next GetModuleAccessAsync call will rebuild its per-company snapshot lazily.
        // The previous source is not disposed: a concurrent GetModuleAccessAsync may still be reading its Token.
        var previous = Interlocked.Exchange(ref _moduleVersionCts, new CancellationTokenSource());
        previous.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    /// SPEC 48 B: Dapper query that resolves, for the given company, every (ControllerName, ModuleName, IsEnabled)
    /// triple that drives <see cref="GetModuleAccessAsync"/>. PostgreSQL syntax (fork <c>main_postgresql</c>):
    /// lowercase identifiers and a boolean expression for <c>IsEnabled</c>. Rows are ordered so the
    /// "first owning module" reported in <c>MODULE_NOT_ENABLED</c> is stable.
    /// </summary>
    private const string ModuleAccessQuerySql = """
        SELECT so.controllername AS ControllerName,
               sm.name           AS ModuleName,
               (sm.isactive AND cm.id IS NOT NULL) AS IsEnabled
        FROM security.systemoptions so
        INNER JOIN admin.systemmodules sm ON sm.id = so.moduleid AND sm.gcrecord = 0
        LEFT JOIN admin.companymodules cm ON cm.moduleid = sm.id AND cm.companyid = @CompanyId
                                         AND cm.isactive = TRUE AND cm.gcrecord = 0
        WHERE so.gcrecord = 0 AND so.controllername IS NOT NULL AND so.controllername <> ''
        ORDER BY so.controllername, sm.name
        """;

    /// <summary>
    /// Maps the Dapper row of <see cref="ModuleAccessQuerySql"/> to a flat projection.
    /// </summary>
    private sealed class ModuleAccessSqlRow
    {
        public string ControllerName { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
    }

    /// <summary>
    /// Maps a single <see cref="PermissionFlags"/> bit to the corresponding boolean in the snapshot.
    /// </summary>
    private static bool EvaluateFlag(PermissionFlagsSnapshot snapshot, PermissionFlags flag) => flag switch
    {
        PermissionFlags.CanRead     => snapshot.CanRead,
        PermissionFlags.CanCreate   => snapshot.CanCreate,
        PermissionFlags.CanUpdate   => snapshot.CanUpdate,
        PermissionFlags.CanDelete   => snapshot.CanDelete,
        PermissionFlags.CanDownload => snapshot.CanDownload,
        PermissionFlags.CanExport   => snapshot.CanExport,
        PermissionFlags.CanExecute  => snapshot.CanExecute,
        _ => false
    };

    /// <summary>
    /// Produces a normalized set of possible controller names to tolerate singular and plural variations.
    /// </summary>
    /// <param name="controllerName">The raw controller name.</param>
    /// <returns>A normalized list of lookup keys.</returns>
    private static IReadOnlyCollection<string> NormalizeControllerNames(string controllerName)
    {
        var trimmed = (controllerName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Array.Empty<string>();
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            trimmed,
            trimmed.Replace("Controller", string.Empty, StringComparison.OrdinalIgnoreCase)
        };

        foreach (var candidate in candidates.ToArray())
        {
            if (candidate.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(candidate[..^3] + "y");
            }
            else if (candidate.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(candidate[..^1]);
            }
            else
            {
                candidates.Add(candidate + "s");
            }
        }

        return candidates.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
    }

    /// <summary>
    /// Snapshot of the seven permission flags for a single resource, OR-merged across all
    /// roles the user holds in the current tenant.
    /// </summary>
    private readonly record struct PermissionFlagsSnapshot(
        bool CanRead,
        bool CanCreate,
        bool CanUpdate,
        bool CanDelete,
        bool CanDownload,
        bool CanExport,
        bool CanExecute);
}
