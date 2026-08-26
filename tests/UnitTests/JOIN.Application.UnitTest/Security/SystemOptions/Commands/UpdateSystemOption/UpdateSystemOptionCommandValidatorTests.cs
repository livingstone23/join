using FluentAssertions;
using JOIN.Application.UseCases.Security.SystemOptions.Commands;

namespace JOIN.Application.UnitTest.Security.SystemOptions.Commands.UpdateSystemOption;

/// <summary>
/// Tests for the FluentValidation rules on the system option update command.
/// Mirrors CreateSystemOptionCommandValidatorTests per SPEC 21.
/// </summary>
public sealed class UpdateSystemOptionCommandValidatorTests
{
    private static UpdateSystemOptionCommand BuildValidCommand() => new(
        Id: Guid.NewGuid(),
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

    [Fact]
    public void Validate_WhenOrderMenuIsNegative_ShouldHaveError()
    {
        var validator = new UpdateSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { OrderMenu = -1 };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateSystemOptionCommand.OrderMenu));
    }

    [Fact]
    public void Validate_WhenOrderMenuExceedsMax_ShouldHaveError()
    {
        var validator = new UpdateSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { OrderMenu = 10001 };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateSystemOptionCommand.OrderMenu));
    }

    [Fact]
    public void Validate_WhenIconExceedsMaxLength_ShouldHaveError()
    {
        var validator = new UpdateSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { Icon = new string('a', 101) };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateSystemOptionCommand.Icon));
    }

    [Fact]
    public void Validate_WhenControllerNameExceedsMaxLength_ShouldHaveError()
    {
        var validator = new UpdateSystemOptionCommandValidator();
        var cmd = BuildValidCommand() with { ControllerName = new string('c', 251) };
        var result = validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateSystemOptionCommand.ControllerName));
    }

    [Fact]
    public void Validate_WhenCommandIsValid_ShouldPass()
    {
        var validator = new UpdateSystemOptionCommandValidator();
        var result = validator.Validate(BuildValidCommand());
        result.IsValid.Should().BeTrue();
    }
}
