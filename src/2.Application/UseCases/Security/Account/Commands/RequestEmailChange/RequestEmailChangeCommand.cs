using JOIN.Application.Common;
using MediatR;



namespace JOIN.Application.UseCases.Security.Account.Commands.RequestEmailChange;



/// <summary>
/// Represents the command used to request an email change confirmation for the current authenticated user.
/// The corresponding handler must delegate confirmation delivery to <c>IEmailService</c> or <c>INotificationService</c>
/// and must not implement SMTP logic directly in the Application layer.
/// </summary>
public sealed record RequestEmailChangeCommand : IRequest<Response<bool>>
{
    /// <summary>
    /// Gets the unique identifier of the authenticated user extracted from claims.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the new email address that must be confirmed.
    /// </summary>
    public string NewEmail { get; init; } = string.Empty;
}