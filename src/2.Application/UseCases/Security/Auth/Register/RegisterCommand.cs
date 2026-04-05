using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Auth.Register;

/// <summary>
/// Command used to register a new application user.
/// </summary>
public record RegisterCommand : IRequest<Response<RegisterResponseDto>>
{
    /// <summary>
    /// Gets or sets the email address of the new user.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the raw password selected by the user.
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the first name of the new user.
    /// </summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the last name of the new user.
    /// </summary>
    public string LastName { get; init; } = string.Empty;
}
