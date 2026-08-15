using FluentValidation;

namespace JOIN.Application.UseCases.Security.Account.Commands.SetupMfa;

/// <summary>
/// Reserved for future request constraints. Empty-body command today.
/// </summary>
public sealed class SetupMfaCommandValidator : AbstractValidator<SetupMfaCommand>
{
    public SetupMfaCommandValidator()
    {
    }
}
