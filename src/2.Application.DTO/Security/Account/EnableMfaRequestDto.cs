namespace JOIN.Application.DTO.Security.Account;

/// <summary>
/// Request payload for <c>POST /api/v1/account/mfa/enable</c>.
/// </summary>
public sealed record EnableMfaRequestDto
{
    /// <summary>
    /// 6-digit TOTP code from the authenticator app that proves the user finished enrolment.
    /// </summary>
    public string Code { get; init; } = string.Empty;
}
