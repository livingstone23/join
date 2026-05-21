namespace JOIN.Application.DTO.Security.Auth;



/// <summary>
/// Represents the request payload used to reset a password with a recovery token.
/// </summary>
public sealed record ResetPasswordRequestDto
{
    /// <summary>
    /// Gets the one-time recovery token sent to the user's email address.
    /// </summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// Gets the new password that will replace the current one.
    /// </summary>
    public string NewPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets the password confirmation value used to verify the intended password.
    /// </summary>
    public string ConfirmPassword { get; init; } = string.Empty;
}