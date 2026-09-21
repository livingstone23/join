using FluentValidation;

namespace JOIN.Application.UseCases.Security.Auth.MfaChallenge.SendMfaChallenge;

/// <summary>
/// Validates the inbound <see cref="SendMfaChallengeCommand"/>'s shape. Business rules
/// (unknown/non-resendable method, expired/missing challenge, cooldown) live in the handler
/// so they can surface as the spec's dedicated error codes.
/// </summary>
public sealed class SendMfaChallengeCommandValidator : AbstractValidator<SendMfaChallengeCommand>
{
    public SendMfaChallengeCommandValidator()
    {
        RuleFor(command => command.ChallengeToken)
            .NotEmpty();

        RuleFor(command => command.Method)
            .NotEmpty();
    }
}
