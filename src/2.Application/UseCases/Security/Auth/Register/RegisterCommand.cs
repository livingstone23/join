using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Auth.Register;

/// <summary>
/// Represents the registration command used to create a new application user account.
/// The request encapsulates the identity fields required to provision the user and return a standardized registration result.
/// </summary>
public record RegisterCommand : IRequest<Response<RegisterResponseDto>>
{
    /// <summary>
    /// Gets or sets the email address that will be assigned to the new user account.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the raw password selected for the new user account.
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the first name that will be stored for the new user profile.
    /// </summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the last name that will be stored for the new user profile.
    /// </summary>
    public string LastName { get; init; } = string.Empty;
}
