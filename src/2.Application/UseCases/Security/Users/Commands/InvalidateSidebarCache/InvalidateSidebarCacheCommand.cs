using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.Users.Commands.InvalidateSidebarCache;

/// <summary>
/// Represents the payload used to clear one or more cached entries for a specific user and company scope.
/// The target cache can be provided through either <c>cacheKey</c> or <c>cleanCache</c> in the HTTP request body.
/// </summary>
public sealed record CleanCacheCommand : IRequest<Response<bool>>
{
    /// <summary>
    /// Gets the unique identifier of the user whose cache entries should be removed.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the unique identifier of the company scope associated with the cached entries.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets the requested cache target when the client sends the preferred <c>cacheKey</c> property.
    /// Accepted values are <c>sidebar</c>, <c>permission</c>, and <c>all</c>.
    /// </summary>
    public string? CacheKey { get; init; }

    /// <summary>
    /// Gets the requested cache target when the client sends the legacy or alternative <c>cleanCache</c> property.
    /// Accepted values are <c>sidebar</c>, <c>permission</c>, and <c>all</c>.
    /// </summary>
    public string? CleanCache { get; init; }

    /// <summary>
    /// Gets the normalized target cache key to be processed by the handler.
    /// </summary>
    public string TargetKey => string.IsNullOrWhiteSpace(CacheKey)
        ? (CleanCache ?? string.Empty)
        : CacheKey;
}
