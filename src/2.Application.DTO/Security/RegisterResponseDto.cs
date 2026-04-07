namespace JOIN.Application.DTO.Security;

/// <summary>
/// Represents the response payload returned after a user account is successfully registered.
/// This DTO gives the caller the minimum identity information required to confirm the registration outcome.
/// </summary>
public record RegisterResponseDto
{
    /// <summary>
    /// Gets the unique identifier assigned to the newly created user account.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the email address stored for the newly registered user account.
    /// </summary>
    public string Email { get; init; } = string.Empty;
}
