namespace JOIN.Application.DTO.Security.Auth;



/// <summary>
/// Represents the request payload used to (re)send the email code for a pending MFA login
/// challenge.
/// </summary>
public sealed record SendMfaChallengeRequestDto
{
    /// <summary>
    /// Gets the opaque challenge token returned by <c>POST /Users/login</c>.
    /// </summary>
    public string ChallengeToken { get; init; } = string.Empty;

    /// <summary>
    /// Gets the method to send a fresh code for. Only "email" is resendable — "totp" is
    /// rejected with <c>CHALLENGE_METHOD_NOT_RESENDABLE</c>.
    /// </summary>
    public string Method { get; init; } = string.Empty;
}
