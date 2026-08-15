using FluentAssertions;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

namespace JOIN.Application.UnitTest.Security.RoleSystemOptions.Commands.CreateRoleSystemOption;

/// <summary>
/// Tests for the FluentValidation rules on the role-system-option creation command.
/// Covers the SPEC 22 field additions (CanDownload, CanExport, CanExecute, IsVisibleMenu, OrderMenu).
/// </summary>
public sealed class CreateRoleSystemOptionCommandValidatorTests
{
    private static CreateRoleSystemOptionCommand BuildValidCommand() => new(
        CompanyId: Guid.NewGuid(),
        RoleId: Guid.NewGuid(),
        SystemOptionId: Guid.NewGuid(),
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
    /// OrderMenu below 0 is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenOrderMenuIsNegative_ShouldHaveError()
    {
        var validator = new CreateRoleSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { OrderMenu = -1 };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateRoleSystemOptionCommand.OrderMenu));
    }

    /// <summary>
    /// OrderMenu above 10000 is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenOrderMenuExceedsMax_ShouldHaveError()
    {
        var validator = new CreateRoleSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { OrderMenu = 10001 };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateRoleSystemOptionCommand.OrderMenu));
    }

    /// <summary>
    /// OrderMenu = null is allowed (validator skips the rule when the value is null).
    /// </summary>
    [Fact]
    public void Validate_WhenOrderMenuIsNull_ShouldPass()
    {
        var validator = new CreateRoleSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { OrderMenu = null };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// OrderMenu = 0 passes (boundary value).
    /// </summary>
    [Fact]
    public void Validate_WhenOrderMenuIsZero_ShouldPass()
    {
        var validator = new CreateRoleSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { OrderMenu = 0 };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Happy path: valid command with all defaults passes validation.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandIsValid_ShouldPass()
    {
        var validator = new CreateRoleSystemOptionCommandValidator();
        var result = validator.Validate(BuildValidCommand());
        result.IsValid.Should().BeTrue();
    }
}