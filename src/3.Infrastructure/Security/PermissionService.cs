using JOIN.Application.Interface;
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
    /// Validates whether the current user has permission to execute the requested action.
    /// </summary>
    /// <param name="userId">The authenticated user identifier.</param>
    /// <param name="companyId">The active company identifier.</param>
    /// <param name="resourceName">The controller or resource name being requested.</param>
    /// <param name="actionType">The HTTP method associated with the request.</param>
    /// <returns><c>true</c> when access is granted; otherwise, <c>false</c>.</returns>
    public async Task<bool> HasPermissionAsync(string userId, string companyId, string resourceName, string actionType)
    {
        if (!Guid.TryParse(userId, out var parsedUserId) || !Guid.TryParse(companyId, out var parsedCompanyId))
        {
            return false;
        }

        var cacheKey = $"permissions:{companyId}:{userId}";
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
                return new Dictionary<string, PermissionFlags>(StringComparer.OrdinalIgnoreCase);
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
                                  roleOption.CanDelete
                              })
                .ToListAsync();

            var map = new Dictionary<string, PermissionFlags>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var normalizedNames = NormalizeControllerNames(row.ControllerName);
                foreach (var name in normalizedNames)
                {
                    map.TryGetValue(name, out var currentFlags);
                    map[name] = new PermissionFlags(
                        currentFlags.CanRead || row.CanRead,
                        currentFlags.CanCreate || row.CanCreate,
                        currentFlags.CanUpdate || row.CanUpdate,
                        currentFlags.CanDelete || row.CanDelete);
                }
            }

            return map;
        });

        var normalizedResourceNames = NormalizeControllerNames(resourceName);
        foreach (var name in normalizedResourceNames)
        {
            if (!permissions!.TryGetValue(name, out var flags))
            {
                continue;
            }

            return actionType.ToUpperInvariant() switch
            {
                "GET" or "HEAD" => flags.CanRead,
                "POST" => flags.CanCreate,
                "PUT" or "PATCH" => flags.CanUpdate,
                "DELETE" => flags.CanDelete,
                _ => false
            };
        }

        return false;
    }

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

    private readonly record struct PermissionFlags(bool CanRead, bool CanCreate, bool CanUpdate, bool CanDelete);
}
