namespace JOIN.Application.DTO.Security.Auth;



/// <summary>
/// Represents the request payload used to verify a pending MFA login challenge and, on
/// success, obtain the real JWT.
/// </summary>
public sealed record VerifyMfaChallengeRequestDto
{
    /// <summary>
    /// Gets the opaque challenge token returned by <c>POST /Users/login</c>.
    /// </summary>
    public string ChallengeToken { get; init; } = string.Empty;

    /// <summary>
    /// Gets the method used for this verification attempt ("email" | "totp").
    /// </summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>
    /// Gets the code to verify — a live TOTP code for "totp", or the emailed code for "email".
    /// </summary>
    public string Code { get; init; } = string.Empty;
}
