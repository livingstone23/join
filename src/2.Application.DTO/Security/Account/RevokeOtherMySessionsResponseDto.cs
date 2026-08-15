namespace JOIN.Application.DTO.Security.Account;

/// <summary>
/// Result of <c>POST /api/v1/account/sessions/revoke-others</c>.
/// Reports the counters so callers can confirm what was closed.
/// </summary>
public sealed record RevokeOtherMySessionsResponseDto
{
    /// <summary>
    /// Number of <c>Security.UserConnectionLogs</c> rows closed
    /// (<c>IsActiveSession = 0</c>, <c>DisconnectionDate</c> set).
    /// </summary>
    public int RevokedConnections { get; init; }

    /// <summary>
    /// Number of <c>Security.UserRefreshTokens</c> rows marked revoked
    /// (<c>IsRevoked = 1</c>). Excludes the current refresh token by design.
    /// </summary>
    public int RevokedTokens { get; init; }
}
