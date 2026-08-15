namespace JOIN.Application.DTO.Security.Account;

/// <summary>
/// Request payload for <c>POST /api/v1/account/confirm-email-change</c>
/// (SPEC 26 / F7). The token is the value delivered by the email-change request flow
/// (<c>POST /account/request-email-change</c>).
/// </summary>
public sealed record ConfirmEmailChangeRequestDto
{
    /// <summary>
    /// The intended new email address. Must match the email originally requested.
    /// </summary>
    public string NewEmail { get; init; } = string.Empty;

    /// <summary>
    /// The raw token (URL-encoded in the email body, decoded on the client).
    /// </summary>
    public string Token { get; init; } = string.Empty;
}
