using FluentValidation;

namespace JOIN.Application.UseCases.Security.Account.Commands.RevokeMySession;

/// <summary>
/// Validates the inbound <see cref="RevokeMySessionCommand"/> before the handler runs.
/// </summary>
public sealed class RevokeMySessionCommandValidator : AbstractValidator<RevokeMySessionCommand>
{
    /// <summary>
    /// Builds the validator with the route-supplied <c>sessionId</c> constraints.
    /// </summary>
    public RevokeMySessionCommandValidator()
    {
        RuleFor(command => command.SessionId)
            .NotEqual(Guid.Empty)
            .WithErrorCode("SESSION_ID_REQUIRED")
            .WithMessage("The session id must be a non-empty GUID.");
    }
}
