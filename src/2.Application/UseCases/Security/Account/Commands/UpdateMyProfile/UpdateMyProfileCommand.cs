using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using MediatR;



namespace JOIN.Application.UseCases.Security.Account.Commands.UpdateMyProfile;



/// <summary>
/// Represents the command used to update profile data for the current authenticated user.
/// </summary>
public sealed record UpdateMyProfileCommand : IRequest<Response<AccountProfileResponseDto>>
{
    /// <summary>
    /// Gets the unique identifier of the authenticated user extracted from claims.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the first name requested for the profile update.
    /// </summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the last name requested for the profile update.
    /// </summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the avatar URL requested for the profile update.
    /// </summary>
    public string? AvatarUrl { get; init; }

    /// <summary>
    /// Gets the communication channels requested for the profile update.
    /// </summary>
    public IReadOnlyCollection<CommunicationChannelDto> CommunicationChannels { get; init; } = Array.Empty<CommunicationChannelDto>();
}