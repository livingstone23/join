using FluentAssertions;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

namespace JOIN.Application.UnitTest.Security.RoleSystemOptions.Commands.UpdateRoleSystemOption;

/// <summary>
/// Tests for the FluentValidation rules on the role-system-option update command.
/// Covers the SPEC 22 field additions (CanDownload, CanExport, CanExecute, IsVisibleMenu, OrderMenu).
/// </summary>
public sealed class UpdateRoleSystemOptionCommandValidatorTests
{
    private static UpdateRoleSystemOptionCommand BuildValidCommand() => new(
        Id: Guid.NewGuid(),
        CompanyId: Guid.NewGuid(),
        CanRead: true,
        CanCreate: true,
        CanUpdate: true,
        CanDelete: true,
        CanDownload: true,
        CanExport: true,
        CanExecute: true,
        IsVisibleMenu: true,
        OrderMenu: 0);

    /// <summary>
    /// Id = Guid.Empty is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenIdIsEmpty_ShouldHaveError()
    {
        var validator = new UpdateRoleSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { Id = Guid.Empty };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateRoleSystemOptionCommand.Id));
    }

    /// <summary>
    /// OrderMenu below 0 is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenOrderMenuIsNegative_ShouldHaveError()
    {
        var validator = new UpdateRoleSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { OrderMenu = -1 };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateRoleSystemOptionCommand.OrderMenu));
    }

    /// <summary>
    /// OrderMenu above 10000 is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenOrderMenuExceedsMax_ShouldHaveError()
    {
        var validator = new UpdateRoleSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { OrderMenu = 10001 };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateRoleSystemOptionCommand.OrderMenu));
    }

    /// <summary>
    /// OrderMenu = null is allowed.
    /// </summary>
    [Fact]
    public void Validate_WhenOrderMenuIsNull_ShouldPass()
    {
        var validator = new UpdateRoleSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { OrderMenu = null };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Happy path: valid command with all defaults passes validation.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandIsValid_ShouldPass()
    {
        var validator = new UpdateRoleSystemOptionCommandValidator();
        var result = validator.Validate(BuildValidCommand());
        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// SPEC 23: CompanyId is optional on PUT. The handler derives the tenant from the token.
    /// </summary>
    [Fact]
    public void Validate_WhenCompanyIdIsNull_ShouldPass()
    {
        var validator = new UpdateRoleSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { CompanyId = null };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeTrue();
    }
}