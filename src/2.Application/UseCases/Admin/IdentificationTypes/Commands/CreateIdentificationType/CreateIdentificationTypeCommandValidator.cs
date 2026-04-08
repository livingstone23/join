using FluentValidation;

namespace JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;

/// <summary>
/// Validates the payload used to create an identification type.
/// </summary>
public sealed class CreateIdentificationTypeCommandValidator : AbstractValidator<CreateIdentificationTypeCommand>
{
    public CreateIdentificationTypeCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(50)
            .WithMessage("Name is required and must not exceed 50 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.Description))
            .WithMessage("Description must not exceed 200 characters.");

        RuleFor(x => x.ValidationPattern)
            .MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.ValidationPattern))
            .WithMessage("ValidationPattern must not exceed 200 characters.");
    }
}