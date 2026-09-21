using FluentValidation;

namespace JOIN.Application.UseCases.Security.Auth.MfaChallenge.VerifyMfaChallenge;

/// <summary>
/// Validates the inbound <see cref="VerifyMfaChallengeCommand"/>'s shape. Business rules
/// (unknown/unavailable method, expired/missing/locked challenge, invalid code) live in the
/// handler so they can surface as the spec's dedicated error codes.
/// </summary>
public sealed class VerifyMfaChallengeCommandValidator : AbstractValidator<VerifyMfaChallengeCommand>
{
    public VerifyMfaChallengeCommandValidator()
    {
        RuleFor(command => command.ChallengeToken)
            .NotEmpty();

        RuleFor(command => command.Method)
            .NotEmpty();

        RuleFor(command => command.Code)
            .NotEmpty();
    }
}
