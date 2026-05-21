using JOIN.Application.Common;
using MediatR;



namespace JOIN.Application.UseCases.Security.Auth.SetupPassword;



/// <summary>
/// Represents the command used to complete first-time account activation by assigning an initial password.
/// The corresponding handler must delegate token email workflows to <c>IEmailService</c> or <c>INotificationService</c>
/// and must not implement SMTP logic directly in the Application layer.
/// </summary>
public sealed record SetupPasswordCommand : IRequest<Response<bool>>
{
    /// <summary>
    /// Gets the one-time setup token sent to the user.
    /// </summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// Gets the new password requested for the account activation flow.
    /// </summary>
    public string NewPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets the password confirmation value used to verify user input.
    /// </summary>
    public string ConfirmPassword { get; init; } = string.Empty;
}