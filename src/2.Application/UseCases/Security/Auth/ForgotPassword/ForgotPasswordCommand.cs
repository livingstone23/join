using JOIN.Application.Common;
using MediatR;



namespace JOIN.Application.UseCases.Security.Auth.ForgotPassword;



/// <summary>
/// Represents the command used to start a password recovery process for an account.
/// The corresponding handler must delegate recovery-token delivery to <c>IEmailService</c> or <c>INotificationService</c>
/// and must not implement SMTP logic directly in the Application layer.
/// </summary>
public sealed record ForgotPasswordCommand : IRequest<Response<bool>>
{
    /// <summary>
    /// Gets the email address that requested password recovery.
    /// </summary>
    public string Email { get; init; } = string.Empty;
}
