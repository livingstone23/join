namespace JOIN.Application.DTO.Security.Account;

/// <summary>
/// Request payload for <c>POST /api/v1/account/email-otp/disable</c>.
/// </summary>
public sealed record DisableEmailOtpRequestDto
{
    /// <summary>
    /// 6-digit code emailed by <c>POST /api/v1/account/email-otp/send-code</c>.
    /// </summary>
    public string Code { get; init; } = string.Empty;
}
