using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Auth.Refresh;

/// <summary>
/// Command used to rotate a valid refresh token and issue a new authenticated session.
/// </summary>
public record RefreshTokenCommand : IRequest<LoginResponse>
{
    /// <summary>
    /// Gets or sets the refresh token supplied by the client.
    /// </summary>
    public string RefreshToken { get; init; } = string.Empty;
}
