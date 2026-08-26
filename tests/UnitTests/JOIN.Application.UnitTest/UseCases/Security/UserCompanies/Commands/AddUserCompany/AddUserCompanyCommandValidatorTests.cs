using FluentAssertions;
using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Security.UserCompanies.Commands.AddUserCompany;

namespace JOIN.Application.UnitTest.UseCases.Security.UserCompanies.Commands.AddUserCompany;

/// <summary>
/// Validates <see cref="AddUserCompanyCommandValidator"/>: topes y mensajes de error
/// documentados en SPEC 28 / F3.
/// </summary>
public sealed class AddUserCompanyCommandValidatorTests
{
    private readonly AddUserCompanyCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenRoleIdsIsEmpty_ShouldFailWithRequiredMessage()
    {
        var command = new AddUserCompanyCommand(Guid.NewGuid(), Guid.NewGuid(), Array.Empty<Guid>());

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.RoleIds)
            .WithErrorMessage("At least one role is required.");
    }

    [Fact]
    public void Validate_WhenRoleIdsExceedsMaximum_ShouldFailWithTooManyRolesMessage()
    {
        var roleIds = Enumerable.Range(0, AddUserCompanyCommandValidator.MaxRolesPerMembership + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        var command = new AddUserCompanyCommand(Guid.NewGuid(), Guid.NewGuid(), roleIds);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.RoleIds)
            .WithErrorMessage($"TOO_MANY_ROLES: Up to {AddUserCompanyCommandValidator.MaxRolesPerMembership} roles per membership are allowed.");
    }

    [Fact]
    public void Validate_WhenRoleIdsHasDuplicates_ShouldFailWithDuplicateRoleMessage()
    {
        var roleId = Guid.NewGuid();
        var command = new AddUserCompanyCommand(Guid.NewGuid(), Guid.NewGuid(), new[] { roleId, roleId });

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.RoleIds)
            .WithErrorMessage("DUPLICATE_ROLE: Duplicate role ids are not allowed.");
    }
}
