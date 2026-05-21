namespace JOIN.Application.DTO.Security.Account;



/// <summary>
/// Represents the basic self-management profile information returned for the authenticated user.
/// </summary>
public sealed record AccountProfileResponseDto
{
    /// <summary>
    /// Gets the authenticated user's username.
    /// </summary>
    public string UserName { get; init; } = string.Empty;

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
    /// Gets the authenticated user's phone number.
    /// </summary>
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// Gets a value indicating whether the authenticated user's email is confirmed.
    /// </summary>
    public bool EmailConfirmed { get; init; }

    /// <summary>
    /// Gets a value indicating whether the authenticated user's phone number is confirmed.
    /// </summary>
    public bool PhoneNumberConfirmed { get; init; }

    /// <summary>
    /// Gets a value indicating whether multi-factor authentication is enabled for the authenticated user.
    /// </summary>
    public bool IsMfaEnabled { get; init; }

    /// <summary>
    /// Gets a value indicating whether the authenticated user has global super admin privileges.
    /// </summary>
    public bool IsSuperAdmin { get; init; }

    /// <summary>
    /// Gets a value indicating whether the authenticated user has company super admin privileges.
    /// </summary>
    public bool IsSuperAdminCompany { get; init; }

    /// <summary>
    /// Gets the UTC date when the authenticated user account was created.
    /// </summary>
    public DateTime Created { get; init; }

    /// <summary>
    /// Gets the communication channels linked to the profile.
    /// </summary>
    public IReadOnlyCollection<CommunicationChannelDto> CommunicationChannels { get; init; } = Array.Empty<CommunicationChannelDto>();
}