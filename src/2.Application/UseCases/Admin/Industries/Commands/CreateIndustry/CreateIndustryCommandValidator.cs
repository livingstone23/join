using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Industries.Commands;

/// <summary>
/// Validates the payload used to create a tenant-scoped industry.
/// </summary>
public sealed class CreateIndustryCommandValidator : AbstractValidator<CreateIndustryCommand>
{
    public CreateIndustryCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(50)
            .WithMessage("Industry code is required and must not exceed 50 characters.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(150)
            .WithMessage("Industry name is required and must not exceed 150 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => !string.IsNullOrWhiteSpace(x.Description))
            .WithMessage("Industry description must not exceed 500 characters.");
    }
}
