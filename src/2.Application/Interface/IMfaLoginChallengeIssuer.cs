using JOIN.Application.DTO.Security;
using JOIN.Domain.Security;



namespace JOIN.Application.Interface;



/// <summary>
/// Defines the contract responsible for creating a server-side <c>MfaLoginChallenge</c> and shaping
/// the challenge branch of <see cref="LoginResponse"/> when a user has 1+ active 2FA method — the
/// counterpart to <see cref="IAuthenticatedSessionIssuer"/> for the "login stops here" path.
/// </summary>
public interface IMfaLoginChallengeIssuer
{
    /// <summary>
    /// Persists a fresh <c>MfaLoginChallenge</c> for <paramref name="user"/> and returns the
    /// challenge-shaped <see cref="LoginResponse"/> (Token/RefreshToken/Expiration left <c>null</c>,
    /// ChallengeToken/AvailableMethods/PreferredMethod populated). Logs
    /// <see cref="SecurityEventType.LoginMfaRequired"/>.
    /// </summary>
    /// <param name="user">The already-authenticated user (credentials already validated by the caller).</param>
    /// <param name="targetCompanyId">The company requested by the client, if any — re-evaluated later at <c>verify</c>, never resolved here.</param>
    /// <param name="cancellationToken">The cancellation token for the current operation.</param>
    /// <returns>The challenge-shaped login response.</returns>
    Task<LoginResponse> IssueChallengeAsync(ApplicationUser user, Guid? targetCompanyId, CancellationToken cancellationToken);
}
