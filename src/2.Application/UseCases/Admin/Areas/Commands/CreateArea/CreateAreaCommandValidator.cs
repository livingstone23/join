using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Areas.Commands;

/// <summary>
/// Validates the payload used to create a tenant-scoped area.
/// </summary>
public sealed class CreateAreaCommandValidator : AbstractValidator<CreateAreaCommand>
{
    public CreateAreaCommandValidator()
    {
        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .WithMessage("Area name is required and must not exceed 100 characters.");

        RuleFor(x => x.EntityStatusId)
            .NotEmpty()
            .WithMessage("EntityStatusId is required.");
    }
}
