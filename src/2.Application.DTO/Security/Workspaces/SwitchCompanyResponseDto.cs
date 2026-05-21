namespace JOIN.Application.DTO.Security.Workspaces;



/// <summary>
/// Represents the authentication payload returned after switching the active company context.
/// </summary>
public sealed record SwitchCompanyResponseDto
{
    /// <summary>
    /// Gets the selected company identifier applied to the new token context.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets the newly issued JWT access token scoped to the selected company.
    /// </summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>
    /// Gets the newly issued refresh token associated with the switched context.
    /// </summary>
    public string RefreshToken { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC expiration timestamp of the returned access token.
    /// </summary>
    public DateTime AccessTokenExpirationUtc { get; init; }
}