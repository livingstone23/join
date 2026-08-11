using FluentAssertions;
using JOIN.Application.UseCases.Security.RoleCompanies.Commands.UpdateRoleCompany;

namespace JOIN.Application.UnitTest.Security.RoleCompanies.Commands.UpdateRoleCompany;

/// <summary>
/// Tests for the FluentValidation rules on the role-company update command.
/// </summary>
public sealed class UpdateRoleCompanyCommandValidatorTests
{
    /// <summary>
    /// Guid.Empty Id is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenIdIsEmpty_ShouldHaveError()
    {
        var validator = new UpdateRoleCompanyCommandValidator();
        var result = validator.Validate(new UpdateRoleCompanyCommand(Guid.Empty, Guid.NewGuid()));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateRoleCompanyCommand.Id));
    }

    /// <summary>
    /// Guid.Empty RoleId is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenRoleIdIsEmpty_ShouldHaveError()
    {
        var validator = new UpdateRoleCompanyCommandValidator();
        var result = validator.Validate(new UpdateRoleCompanyCommand(Guid.NewGuid(), Guid.Empty));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateRoleCompanyCommand.RoleId));
    }

    /// <summary>
    /// Both fields non-empty passes validation.
    /// </summary>
    [Fact]
    public void Validate_WhenAllFieldsValid_ShouldPass()
    {
        var validator = new UpdateRoleCompanyCommandValidator();
        var result = validator.Validate(new UpdateRoleCompanyCommand(Guid.NewGuid(), Guid.NewGuid()));
        result.IsValid.Should().BeTrue();
    }
}
