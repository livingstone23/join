using FluentValidation;

namespace JOIN.Application.UseCases.Admin.EntityStatuses.Commands;

/// <summary>
/// Validates the payload used to update an administrative entity status.
/// </summary>
public sealed class UpdateEntityStatusCommandValidator : AbstractValidator<UpdateEntityStatusCommand>
{
    public UpdateEntityStatusCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(50)
            .WithMessage("Name is required and must not exceed 50 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.Description))
            .WithMessage("Description must not exceed 200 characters.");

        RuleFor(x => x.Code)
            .GreaterThan(0)
            .WithMessage("Code must be greater than 0.");
    }
}
