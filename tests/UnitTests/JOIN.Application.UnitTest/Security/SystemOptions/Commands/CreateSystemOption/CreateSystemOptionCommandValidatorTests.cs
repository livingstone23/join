using FluentAssertions;
using JOIN.Application.UseCases.Security.SystemOptions.Commands;

namespace JOIN.Application.UnitTest.Security.SystemOptions.Commands.CreateSystemOption;

/// <summary>
/// Tests for the FluentValidation rules on the system option creation command.
/// Covers the SPEC 21 field additions (OrderMenu, Icon, ControllerName).
/// </summary>
public sealed class CreateSystemOptionCommandValidatorTests
{
    private static CreateSystemOptionCommand BuildValidCommand() => new(
        ModuleId: Guid.NewGuid(),
        Name: "Manage Tickets",
        Route: "/tickets/manage",
        Icon: "ticket",
        ParentId: null,
        ControllerName: "Tickets",
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
        var validator = new CreateSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { OrderMenu = -1 };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateSystemOptionCommand.OrderMenu));
    }

    /// <summary>
    /// OrderMenu above 10000 is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenOrderMenuExceedsMax_ShouldHaveError()
    {
        var validator = new CreateSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { OrderMenu = 10001 };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateSystemOptionCommand.OrderMenu));
    }

    /// <summary>
    /// Icon over 100 chars is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenIconExceedsMaxLength_ShouldHaveError()
    {
        var validator = new CreateSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { Icon = new string('a', 101) };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateSystemOptionCommand.Icon));
    }

    /// <summary>
    /// ControllerName over 250 chars is rejected (mirrors SystemOptionConfiguration.HasMaxLength(250)).
    /// </summary>
    [Fact]
    public void Validate_WhenControllerNameExceedsMaxLength_ShouldHaveError()
    {
        var validator = new CreateSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { ControllerName = new string('c', 251) };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateSystemOptionCommand.ControllerName));
    }

    /// <summary>
    /// Happy path: valid command with all defaults passes validation.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandIsValid_ShouldPass()
    {
        var validator = new CreateSystemOptionCommandValidator();
        var result = validator.Validate(BuildValidCommand());
        result.IsValid.Should().BeTrue();
    }
}