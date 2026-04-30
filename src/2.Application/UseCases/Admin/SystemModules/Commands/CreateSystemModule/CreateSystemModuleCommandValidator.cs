using FluentValidation;



namespace JOIN.Application.UseCases.Admin.SystemModules.Commands;



/// <summary>
/// Validates the payload used to create a system module.
/// </summary>
public sealed class CreateSystemModuleCommandValidator : AbstractValidator<CreateSystemModuleCommand>
{
    public CreateSystemModuleCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .WithMessage("Name is required and must not exceed 100 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(250)
            .When(x => !string.IsNullOrWhiteSpace(x.Description))
            .WithMessage("Description must not exceed 250 characters.");

        RuleFor(x => x.Icon)
            .MaximumLength(100)
            .When(x => !string.IsNullOrWhiteSpace(x.Icon))
            .WithMessage("Icon must not exceed 100 characters.");

        RuleFor(x => x.Order)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Order.HasValue)
            .WithMessage("Order must be greater than or equal to 0 when provided.");
    }
}