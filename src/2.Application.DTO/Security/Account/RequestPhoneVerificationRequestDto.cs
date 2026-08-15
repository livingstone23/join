namespace JOIN.Application.DTO.Security.Account;

/// <summary>
/// Request payload for <c>POST /api/v1/account/verify-phone/request</c>
/// (SPEC 26 / F8). The phone number MUST be E.164-formatted (<c>^\+[1-9]\d{1,14}$</c>).
/// </summary>
public sealed record RequestPhoneVerificationRequestDto
{
    /// <summary>
    /// E.164-formatted phone number (e.g. "+14155552671").
    /// </summary>
    public string PhoneNumber { get; init; } = string.Empty;
}
