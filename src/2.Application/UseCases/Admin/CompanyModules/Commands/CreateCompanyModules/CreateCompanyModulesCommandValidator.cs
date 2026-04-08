using FluentValidation;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Validates the payload used to create a tenant-scoped company module assignment.
/// </summary>
public sealed class CreateCompanyModulesCommandValidator : AbstractValidator<CreateCompanyModulesCommand>
{
    public CreateCompanyModulesCommandValidator()
    {
        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");

        RuleFor(x => x.ModuleId)
            .NotEmpty()
            .WithMessage("ModuleId is required.");
    }
}
