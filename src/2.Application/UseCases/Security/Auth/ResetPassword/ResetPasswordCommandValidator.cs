using FluentValidation;

namespace JOIN.Application.UseCases.Security.Auth.ResetPassword;

/// <summary>
/// Validates <see cref="ResetPasswordCommand"/>: email well-formed, token + new password
/// required, confirmation matches new password.
/// </summary>
public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
        RuleFor(x => x.ConfirmPassword).Equal(x => x.NewPassword);
    }
}
