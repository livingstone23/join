namespace JOIN.Application.DTO.Security.Account;

/// <summary>
/// Result of <c>DELETE /api/v1/account/sessions/{sessionId}</c>.
/// Reports counters so callers can verify which source table the session came from.
/// </summary>
public sealed record RevokeMySessionResponseDto
{
    /// <summary>
    /// Number of <c>Security.UserConnectionLogs</c> rows closed (<c>IsActiveSession = 0</c>).
    /// Always 0 or 1 for the single-revoke flow.
    /// </summary>
    public int RevokedConnections { get; init; }

    /// <summary>
    /// Number of <c>Security.UserRefreshTokens</c> rows marked revoked (<c>IsRevoked = 1</c>).
    /// Always 0 or 1 for the single-revoke flow.
    /// </summary>
    public int RevokedTokens { get; init; }

    /// <summary>
    /// Maps the supplied sessionId to the underlying source table.
    /// </summary>
    public string SessionType { get; init; } = string.Empty;
}
