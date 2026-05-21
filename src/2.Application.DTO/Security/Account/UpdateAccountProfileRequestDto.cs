namespace JOIN.Application.DTO.Security.Account;



/// <summary>
/// Represents the request payload used to update basic profile information for the authenticated user.
/// Email and password are intentionally excluded from this contract.
/// </summary>
public sealed record UpdateAccountProfileRequestDto
{
    /// <summary>
    /// Gets the first name to store in the user profile.
    /// </summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the last name to store in the user profile.
    /// </summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the avatar URL to store in the user profile.
    /// </summary>
    public string? AvatarUrl { get; init; }

    /// <summary>
    /// Gets the communication channels to upsert for the profile.
    /// </summary>
    public IReadOnlyCollection<CommunicationChannelDto> CommunicationChannels { get; init; } = Array.Empty<CommunicationChannelDto>();
}