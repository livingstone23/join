using FluentValidation;

namespace JOIN.Application.UseCases.Security.Auth.ForgotPassword;

/// <summary>
/// Validates <see cref="ForgotPasswordCommand"/>: email must be present and well-formed.
/// Account-existence checks live in the handler to avoid leaking enumeration info through
/// validation messages.
/// </summary>
public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();
    }
}
