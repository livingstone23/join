namespace JOIN.Application.DTO.Security.Account;

/// <summary>
/// Response payload for <c>POST /api/v1/account/mfa/setup</c> (SPEC 26 / F6).
/// Returns the freshly minted secret, the otpauth provisioning URI for the QR code,
/// and the 10 plaintext recovery codes. The codes are only ever visible once.
/// </summary>
public sealed record SetupMfaResponseDto
{
    /// <summary>
    /// Base32 secret the user enrols in their authenticator app.
    /// </summary>
    public string Secret { get; init; } = string.Empty;

    /// <summary>
    /// <c>otpauth://</c> provisioning URI for QR code generation on the client.
    /// </summary>
    public string QrCodeUri { get; init; } = string.Empty;

    /// <summary>
    /// 10 fresh recovery codes shown to the user right after this response.
    /// </summary>
    public IReadOnlyList<string> RecoveryCodes { get; init; } = Array.Empty<string>();
}
