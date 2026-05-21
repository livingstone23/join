namespace JOIN.Application.DTO.Security.Auth;



/// <summary>
/// Represents the request payload used to complete first-time user activation by setting an initial password.
/// </summary>
public sealed record SetupPasswordRequestDto
{
    /// <summary>
    /// Gets the one-time setup token sent to the user's email address.
    /// </summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// Gets the new password that will be assigned to the account.
    /// </summary>
    public string NewPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets the password confirmation value used to avoid typing mistakes.
    /// </summary>
    public string ConfirmPassword { get; init; } = string.Empty;
}