using FluentValidation;

namespace JOIN.Application.UseCases.Security.Auth.SetupPassword;

/// <summary>
/// Validates <see cref="SetupPasswordCommand"/>: same rules as reset, plus email well-formed.
/// </summary>
public sealed class SetupPasswordCommandValidator : AbstractValidator<SetupPasswordCommand>
{
    public SetupPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
        RuleFor(x => x.ConfirmPassword).Equal(x => x.NewPassword);
    }
}
