namespace JOIN.Application.DTO.Security.Auth;



/// <summary>
/// Represents the request payload used to initiate a password recovery workflow.
/// </summary>
public sealed record ForgotPasswordRequestDto
{
    /// <summary>
    /// Gets the email address of the account requesting password recovery.
    /// </summary>
    public string Email { get; init; } = string.Empty;
}