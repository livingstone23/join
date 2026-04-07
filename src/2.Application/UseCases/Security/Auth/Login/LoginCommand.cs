using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Auth.Login;

/// <summary>
/// Represents the authentication command used to validate user credentials and resolve the effective company context for the resulting session.
/// This request is the main entry point for opening a JWT-backed session in the security module.
/// </summary>
public record LoginCommand : IRequest<LoginResponse>
{
    /// <summary>
    /// Gets or sets the email address supplied by the client as the login identifier.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the raw password supplied by the client for credential validation.
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional company identifier that should become active during login when the user belongs to multiple companies.
    /// </summary>
    public Guid? TargetCompanyId { get; init; }
}
