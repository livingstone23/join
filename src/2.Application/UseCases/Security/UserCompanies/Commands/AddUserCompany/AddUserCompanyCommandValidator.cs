using FluentValidation;

namespace JOIN.Application.UseCases.Security.UserCompanies.Commands.AddUserCompany;

/// <summary>
/// Validates <see cref="AddUserCompanyCommand"/>: identifiers not empty, role list
/// non-empty with a 20-item cap and no duplicates. The cap mirrors the invite flow
/// (<c>InviteUserCommandValidator.MaxRolesPerInvite</c>) so the membership endpoint
/// cannot bypass it with a much larger payload. Codes <c>TOO_MANY_ROLES</c> and
/// <c>DUPLICATE_ROLE</c> surface in the per-field messages; the response status is
/// always 400 (<c>VALIDATION_FAILED</c>) at the HTTP boundary — see SPEC 28 / F8.
/// </summary>
public sealed class AddUserCompanyCommandValidator : AbstractValidator<AddUserCompanyCommand>
{
    /// <summary>Hard cap on roles per add-membership request.</summary>
    public const int MaxRolesPerMembership = 20;

    public AddUserCompanyCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");

        RuleFor(x => x.RoleIds)
            .NotNull()
            .Must(ids => ids is not null && ids.Count > 0)
            .WithMessage("At least one role is required.");

        RuleFor(x => x.RoleIds)
            .Must(ids => ids is null || ids.Count <= MaxRolesPerMembership)
            .WithMessage($"TOO_MANY_ROLES: Up to {MaxRolesPerMembership} roles per membership are allowed.")
            .When(x => x.RoleIds is not null);

        RuleFor(x => x.RoleIds)
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("DUPLICATE_ROLE: Duplicate role ids are not allowed.")
            .When(x => x.RoleIds is not null);

        RuleForEach(x => x.RoleIds)
            .NotEqual(Guid.Empty)
            .When(x => x.RoleIds is not null);
    }
}