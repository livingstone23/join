using FluentValidation;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Validates the payload used to update a tenant-scoped company module assignment.
/// </summary>
public sealed class UpdateCompanyModulesCommandValidator : AbstractValidator<UpdateCompanyModulesCommand>
{
    public UpdateCompanyModulesCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");
    }
}
