using FluentValidation;



namespace JOIN.Application.UseCases.Security.Auth.Login;



/// <summary>
/// Defines validation rules for <see cref="LoginCommand"/>.
/// </summary>
public class LoginCommandValidator : AbstractValidator<LoginCommand>
{


    /// <summary>
    /// Initializes a new instance of the <see cref="LoginCommandValidator"/> class.
    /// </summary>
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256).WithMessage("Email cannot exceed 256 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(200).WithMessage("Password cannot exceed 200 characters.");

        RuleFor(x => x.TargetCompanyId)
            .Must(companyId => !companyId.HasValue || companyId.Value != Guid.Empty)
            .WithMessage("TargetCompanyId cannot be an empty GUID.");
    }
}
