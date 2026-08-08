using FluentAssertions;
using JOIN.Application.UseCases.Security.Roles.Commands.CreateRole;

namespace JOIN.Application.UnitTest.Security.Roles.Commands.CreateRole;

/// <summary>
/// Tests for the FluentValidation rules on the role creation command.
/// </summary>
public sealed class CreateRoleCommandValidatorTests
{
    /// <summary>
    /// Empty name is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenNameIsEmpty_ShouldHaveError()
    {
        var validator = new CreateRoleCommandValidator();
        var result = validator.Validate(new CreateRoleCommand(string.Empty, null, false));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateRoleCommand.Name));
    }

    /// <summary>
    /// Name over 256 chars is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenNameExceedsMaxLength_ShouldHaveError()
    {
        var validator = new CreateRoleCommandValidator();
        var longName = new string('a', 257);
        var result = validator.Validate(new CreateRoleCommand(longName, null, false));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateRoleCommand.Name));
    }

    /// <summary>
    /// Description over 500 chars is rejected.
    /// </summary>
    [Fact]
    public void Validate_WhenDescriptionExceedsMaxLength_ShouldHaveError()
    {
        var validator = new CreateRoleCommandValidator();
        var longDescription = new string('a', 501);
        var result = validator.Validate(new CreateRoleCommand("Admin", longDescription, false));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateRoleCommand.Description));
    }

    /// <summary>
    /// Happy path: valid name and description pass validation.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandIsValid_ShouldPass()
    {
        var validator = new CreateRoleCommandValidator();
        var result = validator.Validate(new CreateRoleCommand("Admin", "All access", false));
        result.IsValid.Should().BeTrue();
    }
}
