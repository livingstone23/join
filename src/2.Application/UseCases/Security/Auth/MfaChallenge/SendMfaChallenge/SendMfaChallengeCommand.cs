using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.Auth.MfaChallenge.SendMfaChallenge;

/// <summary>
/// Backs <c>POST /api/v1/auth/mfa/challenge/send</c> (SPEC 32 / F6). (Re)sends the email code
/// for a pending MFA login challenge. Does not apply to <c>totp</c> — that method is validated
/// live and has nothing to send.
/// </summary>
public sealed record SendMfaChallengeCommand : ITransactionalCommand<Response<bool>>
{
    /// <summary>The opaque challenge token returned by <c>POST /Users/login</c>.</summary>
    public string ChallengeToken { get; init; } = string.Empty;

    /// <summary>The method to send a fresh code for. Only <c>"email"</c> is resendable today.</summary>
    public string Method { get; init; } = string.Empty;
}
