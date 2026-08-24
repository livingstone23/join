using JOIN.Application.Common;
using MediatR;



namespace JOIN.Application.UseCases.Security.Auth.ResetPassword;



/// <summary>
/// Represents the command used to reset an account password through a valid recovery token.
/// </summary>
public sealed record ResetPasswordCommand : IRequest<Response<bool>>
{
    /// <summary>
    /// Gets the email address of the account whose password is being reset. Required so the
    /// Identity token can resolve the user (tokens are not self-describing).
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets the one-time recovery token provided by the user.
    /// </summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// Gets the new password requested for the account.
    /// </summary>
    public string NewPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets the password confirmation value used to validate the requested password.
    /// </summary>
    public string ConfirmPassword { get; init; } = string.Empty;
}