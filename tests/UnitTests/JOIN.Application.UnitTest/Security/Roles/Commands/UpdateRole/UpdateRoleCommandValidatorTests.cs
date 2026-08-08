using FluentAssertions;
using JOIN.Application.UseCases.Security.Roles.Commands.UpdateRole;

namespace JOIN.Application.UnitTest.Security.Roles.Commands.UpdateRole;

/// <summary>
/// Tests for the FluentValidation rules on the role update command.
/// Mirrors the CreateRoleCommandValidator constraints.
/// </summary>
public sealed class UpdateRoleCommandValidatorTests
{
    [Fact]
    public void Validate_WhenNameIsEmpty_ShouldHaveError()
    {
        var validator = new UpdateRoleCommandValidator();
        var result = validator.Validate(new UpdateRoleCommand(Guid.NewGuid(), string.Empty, null, false));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateRoleCommand.Name));
    }

    [Fact]
    public void Validate_WhenNameExceedsMaxLength_ShouldHaveError()
    {
        var validator = new UpdateRoleCommandValidator();
        var longName = new string('a', 257);
        var result = validator.Validate(new UpdateRoleCommand(Guid.NewGuid(), longName, null, false));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateRoleCommand.Name));
    }

    [Fact]
    public void Validate_WhenDescriptionExceedsMaxLength_ShouldHaveError()
    {
        var validator = new UpdateRoleCommandValidator();
        var longDescription = new string('a', 501);
        var result = validator.Validate(new UpdateRoleCommand(Guid.NewGuid(), "Admin", longDescription, false));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateRoleCommand.Description));
    }

    [Fact]
    public void Validate_WhenCommandIsValid_ShouldPass()
    {
        var validator = new UpdateRoleCommandValidator();
        var result = validator.Validate(new UpdateRoleCommand(Guid.NewGuid(), "Admin", "All access", false));
        result.IsValid.Should().BeTrue();
    }
}
