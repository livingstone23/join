using FluentValidation;

namespace JOIN.Application.UseCases.Security.Auth.Refresh;

/// <summary>
/// Defines validation rules for <see cref="RefreshTokenCommand"/>.
/// </summary>
public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshTokenCommandValidator"/> class.
    /// </summary>
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.")
            .MaximumLength(500).WithMessage("Refresh token cannot exceed 500 characters.");
    }
}
