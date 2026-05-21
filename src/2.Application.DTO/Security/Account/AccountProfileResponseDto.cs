namespace JOIN.Application.DTO.Security.Account;



/// <summary>
/// Represents the basic self-management profile information returned for the authenticated user.
/// </summary>
public sealed record AccountProfileResponseDto
{
    /// <summary>
    /// Gets the authenticated user's first name.
    /// </summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the authenticated user's last name.
    /// </summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the authenticated user's email address.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets the URL of the avatar image associated with the user profile.
    /// </summary>
    public string? AvatarUrl { get; init; }

    /// <summary>
    /// Gets the communication channels linked to the profile.
    /// </summary>
    public IReadOnlyCollection<CommunicationChannelDto> CommunicationChannels { get; init; } = Array.Empty<CommunicationChannelDto>();
}