using JOIN.Application.DTO.Security;
using JOIN.Domain.Security;



namespace JOIN.Application.Interface;



/// <summary>
/// Defines the contract responsible for resolving the effective company/roles for an already-authenticated
/// user and issuing the resulting session (refresh token + JWT), shared by the direct login path and the
/// MFA challenge verification path so the resolution logic is never duplicated between them.
/// </summary>
public interface IAuthenticatedSessionIssuer
{
    /// <summary>
    /// Resolves the effective company and roles for <paramref name="user"/>, persists a new refresh token,
    /// issues the JWT, and logs the <c>LoginSucceeded</c> security event.
    /// </summary>
    /// <param name="user">The already-authenticated user (credentials already validated by the caller).</param>
    /// <param name="targetCompanyId">The company requested by the client, if any.</param>
    /// <param name="cancellationToken">The cancellation token for the current operation.</param>
    /// <returns>The authenticated session payload, with <c>Token</c>/<c>RefreshToken</c> populated.</returns>
    Task<LoginResponse> IssueAsync(ApplicationUser user, Guid? targetCompanyId, CancellationToken cancellationToken);
}
