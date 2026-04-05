namespace JOIN.Application.DTO.Security;

/// <summary>
/// Represents the successful response payload returned after a user registration.
/// </summary>
public record RegisterResponseDto
{
    /// <summary>
    /// Gets the unique identifier of the created user.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the email address of the created user.
    /// </summary>
    public string Email { get; init; } = string.Empty;
}
