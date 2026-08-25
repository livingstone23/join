using FluentValidation;

namespace JOIN.Application.UseCases.Security.Users.Commands.BulkUpdateUserRoles;

/// <summary>
/// Validates <see cref="BulkUpdateUserRolesCommand"/>. SPEC 28 / F6 caps and rules:
///   * <c>UserIds</c>: non-empty, ≤200, no duplicates, no <c>Guid.Empty</c>.
///   * <c>AddRoleIds</c> / <c>RemoveRoleIds</c>: each can be empty but not both,
///     combined count ≤40, no <c>Guid.Empty</c>, no overlap.
/// Codes in <c>WithMessage</c> surface in the per-field error detail; HTTP status
/// is always 400 (<c>VALIDATION_FAILED</c>) at the boundary — see SPEC 28 / F8.
/// </summary>
public sealed class BulkUpdateUserRolesCommandValidator : AbstractValidator<BulkUpdateUserRolesCommand>
{
    /// <summary>Hard cap on users in a single bulk request.</summary>
    public const int MaxUsersPerBulk = 200;

    /// <summary>Hard cap on the combined count of add + remove role ids per request.</summary>
    public const int MaxRolesPerBulk = 40;

    public BulkUpdateUserRolesCommandValidator()
    {
        RuleFor(x => x.UserIds)
            .NotNull()
            .Must(ids => ids is not null && ids.Count > 0)
            .WithMessage("At least one user id is required.");

        RuleFor(x => x.UserIds)
            .Must(ids => ids is null || ids.Count <= MaxUsersPerBulk)
            .WithMessage($"BULK_TOO_MANY_USERS: Up to {MaxUsersPerBulk} users per bulk request are allowed.")
            .When(x => x.UserIds is not null);

        RuleFor(x => x.UserIds)
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("BULK_DUPLICATE_USER: Duplicate user ids are not allowed.")
            .When(x => x.UserIds is not null);

        RuleForEach(x => x.UserIds)
            .NotEqual(Guid.Empty)
            .When(x => x.UserIds is not null);

        // Both role lists must not be empty together — a no-op request is rejected
        // at the validator rather than silently returning an empty result.
        RuleFor(x => new { x.AddRoleIds, x.RemoveRoleIds })
            .Must(t => (t.AddRoleIds?.Count ?? 0) > 0 || (t.RemoveRoleIds?.Count ?? 0) > 0)
            .WithMessage("BULK_NO_OP: At least one of addRoleIds or removeRoleIds must be non-empty.");

        RuleFor(x => new { x.AddRoleIds, x.RemoveRoleIds })
            .Must(t => (t.AddRoleIds?.Count ?? 0) + (t.RemoveRoleIds?.Count ?? 0) <= MaxRolesPerBulk)
            .WithMessage($"BULK_TOO_MANY_ROLES: Up to {MaxRolesPerBulk} combined role ids per bulk request are allowed.");

        RuleForEach(x => x.AddRoleIds)
            .NotEqual(Guid.Empty)
            .When(x => x.AddRoleIds is not null);

        RuleForEach(x => x.RemoveRoleIds)
            .NotEqual(Guid.Empty)
            .When(x => x.RemoveRoleIds is not null);

        // A roleId in both lists means "add then immediately remove" — useless work
        // and confusing for the operator. Reject at the validator.
        RuleFor(x => new { x.AddRoleIds, x.RemoveRoleIds })
            .Must(t =>
            {
                if (t.AddRoleIds is null || t.RemoveRoleIds is null)
                {
                    return true;
                }
                var adds = new HashSet<Guid>(t.AddRoleIds);
                return !t.RemoveRoleIds.Any(adds.Contains);
            })
            .WithMessage("ROLE_IN_BOTH_LISTS: A role id cannot appear in both addRoleIds and removeRoleIds.");
    }
}