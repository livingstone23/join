namespace JOIN.Application.DTO.Security.Account;



/// <summary>
/// Represents the request payload used by an authenticated user to change the account password.
/// </summary>
public sealed record ChangePasswordRequestDto
{
    /// <summary>
    /// Gets the current password for identity verification.
    /// </summary>
    public string OldPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets the new password requested by the user.
    /// </summary>
    public string NewPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets the confirmation value for the new password.
    /// </summary>
    public string ConfirmPassword { get; init; } = string.Empty;
}