using FluentValidation;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Commands.BulkUpsertRoleSystemOptions;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands.BulkUpsertRoleSystemOptions;

/// <summary>
/// Validates <see cref="BulkUpsertRoleSystemOptionsCommand"/> before the handler runs.
/// Rejects empty/oversized batches, null payload, duplicates, and per-item invalid ids.
/// Error codes surfaced (so the controller can map to status):
///   <c>RoleId required.</c>          → BULK_ROLE_REQUIRED (400)
///   <c>Items must not be empty.</c>  → BULK_EMPTY         (400)
///   <c>Items must not exceed 500.</c>→ BULK_TOO_LARGE     (400)
///   <c>Items contain duplicate SystemOptionId.</c>→ BULK_DUPLICATE_OPTION (400)
/// </summary>
public sealed class BulkUpsertRoleSystemOptionsCommandValidator
    : AbstractValidator<BulkUpsertRoleSystemOptionsCommand>
{
    private const int MaxItems = 500;

    public BulkUpsertRoleSystemOptionsCommandValidator()
    {
        RuleFor(x => x.RoleId)
            .NotEmpty().WithMessage("RoleId required.");

        // Independent rule blocks so a single failing check does not swallow subsequent ones
        // (FluentValidation defaults to CascadeMode.Stop on a chain, but separate RuleFor calls
        // are independent and each accumulates its own errors).
        RuleFor(x => x.Items)
            .NotNull().WithMessage("Items must not be null.");

        RuleFor(x => x.Items)
            .Must(items => items is null || items.Count > 0).WithMessage("BULK_EMPTY");

        RuleFor(x => x.Items)
            .Must(items => items is null || items.Count <= MaxItems)
                .WithMessage($"BULK_TOO_LARGE. Maximum {MaxItems} items per request.");

        RuleFor(x => x.Items)
            .Must(items => items is null
                || items.Select(i => i.SystemOptionId).Distinct().Count() == items.Count)
            .WithMessage("BULK_DUPLICATE_OPTION");

        RuleForEach(x => x.Items)
            .ChildRules(item =>
            {
                item.RuleFor(i => i.SystemOptionId).NotEmpty();
            })
            .When(x => x.Items is not null);
    }
}