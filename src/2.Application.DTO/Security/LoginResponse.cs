namespace JOIN.Application.DTO.Security;

/// <summary>
/// Represents the complete authentication payload returned after a successful login or refresh operation.
/// This DTO centralizes the security session context required by the client, including identity, effective company scope, role membership, and token material.
/// </summary>
public record LoginResponse
{
    /// <summary>
    /// Gets the unique identifier of the authenticated user that owns the session.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the normalized application user name associated with the authenticated session.
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the email address that was used to authenticate and open the current session.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets the effective company identifier resolved for the session.
    /// This value can be <see langword="null"/> when the user has not yet been bound to a valid company context.
    /// </summary>
    public Guid? CompanyId { get; init; }

    /// <summary>
    /// Gets the effective role names resolved for the authenticated session after applying the current security context.
    /// </summary>
    public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the signed JWT access token that must be sent by the client in subsequent authorized requests.
    /// </summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// Gets the refresh token associated with the current authenticated session and used to renew access without re-entering credentials.
    /// </summary>
    public string RefreshToken { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC expiration timestamp for the access token currently assigned to the session.
    /// </summary>
    public DateTime Expiration { get; init; }
}
