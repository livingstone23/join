using FluentAssertions;
using JOIN.Application.UseCases.Security.RoleCompanies.Commands.CreateRoleCompany;

namespace JOIN.Application.UnitTest.Security.RoleCompanies.Commands.CreateRoleCompany;

/// <summary>
/// Tests for the FluentValidation rules on the role-company creation command.
/// </summary>
public sealed class CreateRoleCompanyCommandValidatorTests
{
    /// <summary>
    /// Guid.Empty RoleId is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenRoleIdIsEmpty_ShouldHaveError()
    {
        var validator = new CreateRoleCompanyCommandValidator();
        var result = validator.Validate(new CreateRoleCompanyCommand(Guid.Empty));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateRoleCompanyCommand.RoleId));
    }

    /// <summary>
    /// Valid RoleId passes validation.
    /// </summary>
    [Fact]
    public void Validate_WhenRoleIdIsValid_ShouldPass()
    {
        var validator = new CreateRoleCompanyCommandValidator();
        var result = validator.Validate(new CreateRoleCompanyCommand(Guid.NewGuid()));
        result.IsValid.Should().BeTrue();
    }
}
