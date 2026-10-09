using FluentValidation;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Validates <see cref="RestoreCompanyModuleCommand"/>.
/// </summary>
public sealed class RestoreCompanyModuleCommandValidator : AbstractValidator<RestoreCompanyModuleCommand>
{
    public RestoreCompanyModuleCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
