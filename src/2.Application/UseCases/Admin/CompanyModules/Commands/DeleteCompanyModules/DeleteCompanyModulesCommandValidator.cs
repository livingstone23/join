using FluentValidation;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Validates the payload used to delete a tenant-scoped company module assignment.
/// </summary>
public sealed class DeleteCompanyModulesCommandValidator : AbstractValidator<DeleteCompanyModulesCommand>
{
    public DeleteCompanyModulesCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");
    }
}
