using JOIN.Application.Common;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Caching.Memory;



namespace JOIN.Application.UseCases.Security.Users.Commands.InvalidateSidebarCache;



/// <summary>
/// Handles the removal of cached sidebar and permission entries for a specific user and company scope.
/// </summary>
/// <param name="memoryCache">In-memory cache used to store previously resolved sidebar and permission results.</param>
/// <param name="permissionService">Service exposing the canonical invalidation entry point (<see cref="IPermissionService.InvalidateUserCacheAsync"/>)
/// so the handler does not assemble the permission-snapshot key by hand and risk drifting from the real key (<c>permissions:v2:{companyId}:{userId}</c>, SPEC 17).</param>
public sealed class CleanCacheCommandHandler(IMemoryCache memoryCache, IPermissionService permissionService)
    : IRequestHandler<CleanCacheCommand, Response<bool>>
{
    /// <summary>
    /// Removes the requested cache entry or entries so subsequent requests are rebuilt from the database.
    /// </summary>
    /// <param name="request">The clean-cache payload.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A standardized response indicating whether the requested cache entries were removed successfully.</returns>
    public async Task<Response<bool>> Handle(CleanCacheCommand request, CancellationToken cancellationToken)
    {
        var targetKey = request.TargetKey.Trim().ToLowerInvariant();

        switch (targetKey)
        {
            case "sidebar":
                memoryCache.Remove($"sidebar:{request.CompanyId}:{request.UserId}");
                break;

            case "permission":
            case "permissions":
                await permissionService.InvalidateUserCacheAsync(request.CompanyId, request.UserId, cancellationToken);
                break;

            case "all":
                await permissionService.InvalidateUserCacheAsync(request.CompanyId, request.UserId, cancellationToken);
                memoryCache.Remove($"sidebar:{request.CompanyId}:{request.UserId}");
                break;

            default:
                return new Response<bool>
                {
                    IsSuccess = false,
                    Message = "INVALID_CACHE_KEY",
                    Errors = ["CacheKey must be one of: 'sidebar', 'permission', or 'all'."]
                };
        }

        return new Response<bool>
        {
            IsSuccess = true,
            Message = $"Cache '{targetKey}' cleaned successfully.",
            Data = true
        };
    }
}
