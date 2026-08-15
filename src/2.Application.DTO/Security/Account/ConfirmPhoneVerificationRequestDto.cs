namespace JOIN.Application.DTO.Security.Account;

/// <summary>
/// Request payload for <c>POST /api/v1/account/verify-phone/confirm</c>
/// (SPEC 26 / F8). The 6-digit numeric code is delivered through the matching request call.
/// </summary>
public sealed record ConfirmPhoneVerificationRequestDto
{
    /// <summary>
    /// 6-digit numeric code delivered to the requested phone number.
    /// </summary>
    public string Code { get; init; } = string.Empty;
}
