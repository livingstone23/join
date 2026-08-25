using FluentAssertions;
using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Security.Users.Commands.BulkUpdateUserRoles;

namespace JOIN.Application.UnitTest.UseCases.Security.Users.Commands.BulkUpdateUserRoles;

/// <summary>
/// Validates <see cref="BulkUpdateUserRolesCommandValidator"/>. SPEC 28 / F6 caps
/// and rules: 200 users max, 40 combined roles max, no duplicates, no overlap, no
/// <c>Guid.Empty</c>.
/// </summary>
public sealed class BulkUpdateUserRolesCommandValidatorTests
{
    private readonly BulkUpdateUserRolesCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenUserIdsIsEmpty_ShouldFailWithRequiredMessage()
    {
        var command = new BulkUpdateUserRolesCommand(
            Array.Empty<Guid>(),
            new[] { Guid.NewGuid() },
            Array.Empty<Guid>());

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.UserIds)
            .WithErrorMessage("At least one user id is required.");
    }

    [Fact]
    public void Validate_WhenUserIdsExceedsMaximum_ShouldFailWithTooManyUsersMessage()
    {
        var userIds = Enumerable.Range(0, BulkUpdateUserRolesCommandValidator.MaxUsersPerBulk + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        var command = new BulkUpdateUserRolesCommand(userIds, new[] { Guid.NewGuid() }, Array.Empty<Guid>());

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.UserIds)
            .WithErrorMessage($"BULK_TOO_MANY_USERS: Up to {BulkUpdateUserRolesCommandValidator.MaxUsersPerBulk} users per bulk request are allowed.");
    }

    [Fact]
    public void Validate_WhenUserIdsHasDuplicates_ShouldFailWithDuplicateUserMessage()
    {
        var userId = Guid.NewGuid();
        var command = new BulkUpdateUserRolesCommand(
            new[] { userId, userId },
            new[] { Guid.NewGuid() },
            Array.Empty<Guid>());

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.UserIds)
            .WithErrorMessage("BULK_DUPLICATE_USER: Duplicate user ids are not allowed.");
    }

    [Fact]
    public void Validate_WhenBothRoleListsAreEmpty_ShouldFailWithNoOpMessage()
    {
        var command = new BulkUpdateUserRolesCommand(
            new[] { Guid.NewGuid() },
            Array.Empty<Guid>(),
            Array.Empty<Guid>());

        var result = _validator.TestValidate(command);

        result.ShouldHaveAnyValidationError()
            .WithErrorMessage("BULK_NO_OP: At least one of addRoleIds or removeRoleIds must be non-empty.");
    }

    [Fact]
    public void Validate_WhenCombinedRolesExceedsMaximum_ShouldFailWithTooManyRolesMessage()
    {
        var add = Enumerable.Range(0, BulkUpdateUserRolesCommandValidator.MaxRolesPerBulk)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        var command = new BulkUpdateUserRolesCommand(new[] { Guid.NewGuid() }, add, new[] { Guid.NewGuid() });

        var result = _validator.TestValidate(command);

        result.ShouldHaveAnyValidationError()
            .WithErrorMessage($"BULK_TOO_MANY_ROLES: Up to {BulkUpdateUserRolesCommandValidator.MaxRolesPerBulk} combined role ids per bulk request are allowed.");
    }

    [Fact]
    public void Validate_WhenRoleIdAppearsInBothLists_ShouldFailWithRoleInBothListsMessage()
    {
        var roleId = Guid.NewGuid();
        var command = new BulkUpdateUserRolesCommand(
            new[] { Guid.NewGuid() },
            new[] { roleId },
            new[] { roleId });

        var result = _validator.TestValidate(command);

        result.ShouldHaveAnyValidationError()
            .WithErrorMessage("ROLE_IN_BOTH_LISTS: A role id cannot appear in both addRoleIds and removeRoleIds.");
    }
}