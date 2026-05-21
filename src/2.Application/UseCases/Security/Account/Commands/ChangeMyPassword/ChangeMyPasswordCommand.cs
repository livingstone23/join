using JOIN.Application.Common;
using MediatR;



namespace JOIN.Application.UseCases.Security.Account.Commands.ChangeMyPassword;



/// <summary>
/// Represents the command used by the authenticated user to change the account password.
/// </summary>
public sealed record ChangeMyPasswordCommand : IRequest<Response<bool>>
{
    /// <summary>
    /// Gets the unique identifier of the authenticated user extracted from claims.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the current password used for identity verification.
    /// </summary>
    public string OldPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets the new password requested by the user.
    /// </summary>
    public string NewPassword { get; init; } = string.Empty;

    /// <summary>
    /// Gets the confirmation value for the requested new password.
    /// </summary>
    public string ConfirmPassword { get; init; } = string.Empty;
}