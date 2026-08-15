using FluentAssertions;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

namespace JOIN.Application.UnitTest.Security.RoleSystemOptions.Commands.DeleteRoleSystemOption;

/// <summary>
/// Tests for the FluentValidation rules on the role-system-option delete command.
/// SPEC 23: CompanyId is optional on DELETE — the tenant is derived from the token.
/// </summary>
public sealed class DeleteRoleSystemOptionCommandValidatorTests
{
    private static DeleteRoleSystemOptionCommand BuildValidCommand() => new(
        Id: Guid.NewGuid(),
        CompanyId: null);

    /// <summary>
    /// Id = Guid.Empty is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenIdIsEmpty_ShouldHaveError()
    {
        var validator = new DeleteRoleSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { Id = Guid.Empty };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(DeleteRoleSystemOptionCommand.Id));
    }

    /// <summary>
    /// Happy path: CompanyId null (tenant comes from the token) passes validation.
    /// </summary>
    [Fact]
    public void Validate_WhenCompanyIdIsNull_ShouldPass()
    {
        var validator = new DeleteRoleSystemOptionCommandValidator();
        var cmd = BuildValidCommand();
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// CompanyId provided (legacy clients) is allowed by the validator; the handler
    /// performs the COMPANY_MISMATCH check against the token.
    /// </summary>
    [Fact]
    public void Validate_WhenCompanyIdIsProvided_ShouldPass()
    {
        var validator = new DeleteRoleSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { CompanyId = Guid.NewGuid() };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeTrue();
    }
}