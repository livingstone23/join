using JOIN.Application.Common;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace JOIN.Application.UseCases.Security.Users.Commands.InvalidateSidebarCache;

/// <summary>
/// Handles the invalidation of the cached sidebar menu for a specific user and company scope.
/// </summary>
/// <param name="memoryCache">In-memory cache used to store previously resolved sidebar results.</param>
public sealed class InvalidateSidebarCacheCommandHandler(IMemoryCache memoryCache)
    : IRequestHandler<InvalidateSidebarCacheCommand, Response<bool>>
{
    /// <summary>
    /// Removes the cached sidebar entry so the next request is forced to reload the menu from the database.
    /// </summary>
    /// <param name="request">The invalidation payload.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A standardized response indicating whether the invalidation request completed successfully.</returns>
    public Task<Response<bool>> Handle(InvalidateSidebarCacheCommand request, CancellationToken cancellationToken)
    {
        var cacheKey = $"sidebar:{request.CompanyId}:{request.UserId}";
        memoryCache.Remove(cacheKey);

        return Task.FromResult(new Response<bool>
        {
            IsSuccess = true,
            Message = "Sidebar cache invalidated successfully.",
            Data = true
        });
    }
}
