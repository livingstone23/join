using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.Users.Commands.ForceUserPasswordReset;

/// <summary>
/// Forces a password reset on a target user. Not transactional — the only state
/// changes are an email send and a soft-revoke of refresh tokens (SPEC 27 item 17).
/// </summary>
public sealed record ForceUserPasswordResetCommand(Guid UserId, string? Reason)
    : IRequest<Response<bool>>;
