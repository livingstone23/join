using JOIN.Domain.Security;



namespace JOIN.Application.Interface;



/// <summary>
/// Defines the contract responsible for generating JWT access tokens for authenticated users.
/// </summary>
public interface IJwtTokenGenerator
{

    /// <summary>
    /// Generates a signed access token and a secure refresh token for the specified user session.
    /// </summary>
    /// <param name="user">The authenticated application user.</param>
    /// <param name="companyId">The effective company identifier for the session, if one is available.</param>
    /// <param name="roles">All effective role names for the session.</param>
    /// <returns>A tuple containing the access token, refresh token, and their expiration metadata.</returns>
    (string Token, string RefreshToken, DateTime Expiration, DateTime RefreshTokenExpiration) GenerateToken(ApplicationUser user, Guid? companyId, IEnumerable<string> roles, Guid refreshTokenId);

    /// <summary>
    /// Generates a cryptographically secure refresh token string suitable for persistence
    /// in <c>Security.UserRefreshTokens</c>.
    /// </summary>
    /// <returns>A random Base64-encoded refresh token string.</returns>
    string GenerateRefreshTokenString();

    /// <summary>
    /// Computes the absolute UTC expiration for a refresh token using the configured lifetime.
    /// Exposed so callers can persist the row before generating the access token that
    /// references its identifier.
    /// </summary>
    /// <returns>The UTC instant after which the refresh token should be considered expired.</returns>
    DateTime GetRefreshTokenExpirationUtc();
}
