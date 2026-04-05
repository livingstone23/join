using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Auth.Login;

/// <summary>
/// Command used to authenticate a user and resolve the active tenant context.
/// </summary>
public record LoginCommand : IRequest<LoginResponse>
{
    /// <summary>
    /// Gets or sets the email address used to sign in.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the raw password supplied by the client.
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional target company to activate during login.
    /// </summary>
    public Guid? TargetCompanyId { get; init; }
}
