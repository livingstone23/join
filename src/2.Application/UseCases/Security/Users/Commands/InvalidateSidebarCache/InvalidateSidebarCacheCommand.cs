using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.Users.Commands.InvalidateSidebarCache;

/// <summary>
/// Represents the command used to invalidate the cached sidebar menu for a specific user and company context.
/// The next sidebar request for the same scope will be rebuilt from the database and stored in cache again.
/// </summary>
/// <param name="UserId">The unique identifier of the user whose sidebar cache should be invalidated.</param>
/// <param name="CompanyId">The unique identifier of the company scope associated with the cached sidebar.</param>
public sealed record InvalidateSidebarCacheCommand(Guid UserId, Guid CompanyId)
    : IRequest<Response<bool>>;
