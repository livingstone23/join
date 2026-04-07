using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Auth.Refresh;

/// <summary>
/// Represents the command used to rotate a valid refresh token and issue a renewed authenticated session payload.
/// This request allows the client to continue an active session without forcing the user to log in again with credentials.
/// </summary>
public record RefreshTokenCommand : IRequest<LoginResponse>
{
    /// <summary>
    /// Gets or sets the refresh token supplied by the client as proof that the session is eligible for renewal.
    /// </summary>
    public string RefreshToken { get; init; } = string.Empty;
}
