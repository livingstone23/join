using FluentAssertions;
using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Security.Users.Commands.InviteUser;
using Xunit;

namespace JOIN.Application.UnitTest.UseCases.Security.Users.Commands.InviteUser;

/// <summary>Unit tests for <see cref="InviteUserCommandValidator"/>.</summary>
public sealed class InviteUserCommandValidatorTests
{
    private readonly InviteUserCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenEmailIsInvalid_ShouldFail()
    {
        var cmd = new InviteUserCommand("not-an-email", "John", "Doe", new[] { Guid.NewGuid() });
        var result = _validator.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Validate_WhenRoleIdsAreEmpty_ShouldFail()
    {
        var cmd = new InviteUserCommand("user@example.com", "John", "Doe", Array.Empty<Guid>());
        var result = _validator.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.RoleIds);
    }

    [Fact]
    public void Validate_WhenRoleIdsExceedCap_ShouldFailWithTooManyRoles()
    {
        var ids = Enumerable.Range(0, InviteUserCommandValidator.MaxRolesPerInvite + 1).Select(_ => Guid.NewGuid()).ToArray();
        var cmd = new InviteUserCommand("user@example.com", "John", "Doe", ids);
        var result = _validator.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.RoleIds);
    }

    [Fact]
    public void Validate_WhenRoleIdsHaveDuplicates_ShouldFailWithDuplicateRole()
    {
        var id = Guid.NewGuid();
        var cmd = new InviteUserCommand("user@example.com", "John", "Doe", new[] { id, id });
        var result = _validator.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.RoleIds);
    }
}
