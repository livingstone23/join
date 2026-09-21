using FluentValidation;

namespace JOIN.Application.UseCases.Security.Account.Commands.SetPreferredMfaMethod;

/// <summary>
/// Validates the inbound <see cref="SetPreferredMfaMethodCommand"/>'s shape. Whether the
/// requested method is a known, currently-active one is a business rule enforced by the
/// handler (<c>PREFERRED_METHOD_NOT_ENABLED</c>).
/// </summary>
public sealed class SetPreferredMfaMethodCommandValidator : AbstractValidator<SetPreferredMfaMethodCommand>
{
    public SetPreferredMfaMethodCommandValidator()
    {
        RuleFor(command => command.Method)
            .NotEmpty();
    }
}
