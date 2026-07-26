using JOIN.Application.Interface;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace JOIN.Infrastructure.Security;

/// <summary>
/// Evaluates dynamic controller permissions for the current user and company context.
/// </summary>
/// <param name="dbContext">Database context used to load effective role permissions.</param>
/// <param name="memoryCache">Cache used to reduce repetitive permission queries.</param>
public class PermissionService(ApplicationDbContext dbContext, IMemoryCache memoryCache) : IPermissionService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IMemoryCache _memoryCache = memoryCache;

    /// <summary>
    /// Cache key version prefix. Bumped to <c>v2</c> in SPEC 17 because the cached value
    /// shape changed from 4 booleans to 7 (added CanDownload/CanExport/CanExecute).
    /// Existing caches with the previous key are ignored and re-built on first call.
    /// </summary>
    private const string CacheKeyPrefix = "permissions:v2";

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
