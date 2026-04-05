namespace JOIN.Application.DTO.Security;



/// <summary>
/// Represents the successful authentication payload returned to the client.
/// </summary>
public record LoginResponse
{


    /// <summary>
    /// Gets the unique identifier of the authenticated user.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the user name associated with the session.
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the email address used for the login.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets the effective company identifier resolved for the session, if one is available.
    /// </summary>
    public Guid? CompanyId { get; init; }

    /// <summary>
    /// Gets all effective role names resolved for the authenticated session.
    /// </summary>
    public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the signed access token.
    /// </summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// Gets the refresh token associated with the current authenticated session.
    /// </summary>
    public string RefreshToken { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC expiration date of the token.
    /// </summary>
    public DateTime Expiration { get; init; }


}
