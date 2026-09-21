namespace JOIN.Application.DTO.Security.Account;

/// <summary>
/// Request payload for <c>PUT /api/v1/account/mfa/preferred-method</c>.
/// </summary>
public sealed record SetPreferredMfaMethodRequestDto
{
    /// <summary>
    /// The method to set as preferred ("email" | "totp"). Must already be active for the caller.
    /// </summary>
    public string Method { get; init; } = string.Empty;
}
