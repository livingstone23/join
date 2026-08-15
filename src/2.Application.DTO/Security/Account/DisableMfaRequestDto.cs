namespace JOIN.Application.DTO.Security.Account;

/// <summary>
/// Request payload for <c>POST /api/v1/account/mfa/disable</c>.
/// </summary>
public sealed record DisableMfaRequestDto
{
    /// <summary>
    /// Either a fresh 6-digit TOTP code or a recovery code. The handler tries TOTP first,
    /// then iterates the user's unused recovery codes for a match.
    /// </summary>
    public string Code { get; init; } = string.Empty;
}
