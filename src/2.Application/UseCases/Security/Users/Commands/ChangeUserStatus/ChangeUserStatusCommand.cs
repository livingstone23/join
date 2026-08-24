using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.Users.Commands.ChangeUserStatus;

/// <summary>
/// Activates or deactivates a user inside the caller's tenant. Not
/// <see cref="ITransactionalCommand{TResponse}"/> on purpose: the flip goes through
/// Dapper, and the three follow-up effects (revoke tokens / close connections /
/// invalidate cache) are independent updates that can fail individually without
/// rolling back the status flip — see SPEC 27 Risks.
/// </summary>
public sealed record ChangeUserStatusCommand(Guid UserId, bool IsActive, string Reason)
    : IRequest<Response<bool>>;
