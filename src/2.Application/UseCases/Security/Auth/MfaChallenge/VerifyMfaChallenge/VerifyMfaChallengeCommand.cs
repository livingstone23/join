using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Auth.MfaChallenge.VerifyMfaChallenge;

/// <summary>
/// Backs <c>POST /api/v1/auth/mfa/challenge/verify</c> (SPEC 32 / F6). Validates the supplied
/// code (live TOTP or the emailed code) and, only on success, resolves company/roles and issues
/// the real JWT via <c>IAuthenticatedSessionIssuer</c>.
/// </summary>
public sealed record VerifyMfaChallengeCommand : ITransactionalCommand<Response<LoginResponse>>
{
    /// <summary>The opaque challenge token returned by <c>POST /Users/login</c>.</summary>
    public string ChallengeToken { get; init; } = string.Empty;

    /// <summary>The method used for this attempt ("email" | "totp").</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>The code to verify — a live TOTP code for "totp", or the emailed code for "email".</summary>
    public string Code { get; init; } = string.Empty;
}
