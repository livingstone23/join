using FluentValidation;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Validates the payload used to delete a RoleSystemOption rule.
/// </summary>
public sealed class DeleteRoleSystemOptionCommandValidator : AbstractValidator<DeleteRoleSystemOptionCommand>
{
    public DeleteRoleSystemOptionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

        // SPEC 23: CompanyId is optional on DELETE. The handler always derives the tenant
        // from ICurrentUserService.CompanyId (JWT claim or X-Company-Id header). If the
        // caller still sends a companyId, the handler rejects a mismatch with COMPANY_MISMATCH.
    }
}
