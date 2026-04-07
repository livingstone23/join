using JOIN.Application.Common;
using MediatR;
using Microsoft.Extensions.Caching.Memory;



namespace JOIN.Application.UseCases.Security.Users.Commands.InvalidateSidebarCache;



/// <summary>
/// Handles the removal of cached sidebar and permission entries for a specific user and company scope.
/// </summary>
/// <param name="memoryCache">In-memory cache used to store previously resolved sidebar and permission results.</param>
public sealed class CleanCacheCommandHandler(IMemoryCache memoryCache)
    : IRequestHandler<CleanCacheCommand, Response<bool>>
{
    /// <summary>
    /// Removes the requested cache entry or entries so subsequent requests are rebuilt from the database.
    /// </summary>
    /// <param name="request">The clean-cache payload.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A standardized response indicating whether the requested cache entries were removed successfully.</returns>
    public Task<Response<bool>> Handle(CleanCacheCommand request, CancellationToken cancellationToken)
    {
        var targetKey = request.TargetKey.Trim().ToLowerInvariant();

        switch (targetKey)
        {
            case "sidebar":
                memoryCache.Remove($"sidebar:{request.CompanyId}:{request.UserId}");
                break;

            case "permission":
            case "permissions":
                memoryCache.Remove($"permissions:{request.CompanyId}:{request.UserId}");
                break;

            case "all":
                memoryCache.Remove($"sidebar:{request.CompanyId}:{request.UserId}");
                memoryCache.Remove($"permissions:{request.CompanyId}:{request.UserId}");
                break;

            default:
                return Task.FromResult(new Response<bool>
                {
                    IsSuccess = false,
                    Message = "INVALID_CACHE_KEY",
                    Errors = ["CacheKey must be one of: 'sidebar', 'permission', or 'all'."]
                });
        }

        return Task.FromResult(new Response<bool>
        {
            IsSuccess = true,
            Message = $"Cache '{targetKey}' cleaned successfully.",
            Data = true
        });
    }
}
