using FluentValidation;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Validates the payload used to update a RoleSystemOption rule.
/// </summary>
public sealed class UpdateRoleSystemOptionCommandValidator : AbstractValidator<UpdateRoleSystemOptionCommand>
{
    public UpdateRoleSystemOptionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

        // SPEC 23: CompanyId is optional on PUT. The handler always derives the tenant
        // from ICurrentUserService.CompanyId. If the body sends a companyId, the handler
        // rejects a mismatch with COMPANY_MISMATCH.

        RuleFor(x => x.OrderMenu)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(10000)
            .When(x => x.OrderMenu.HasValue)
            .WithMessage("OrderMenu must be between 0 and 10000.");
    }
}
