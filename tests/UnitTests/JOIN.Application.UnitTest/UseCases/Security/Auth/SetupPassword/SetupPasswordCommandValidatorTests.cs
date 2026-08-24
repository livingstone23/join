using FluentAssertions;
using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Security.Auth.SetupPassword;
using Xunit;

namespace JOIN.Application.UnitTest.UseCases.Security.Auth.SetupPassword;

/// <summary>
/// Unit tests for <see cref="SetupPasswordCommandValidator"/>.
/// </summary>
public sealed class SetupPasswordCommandValidatorTests
{
    private readonly SetupPasswordCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenAllFieldsAreValid_ShouldPass()
    {
        var cmd = new SetupPasswordCommand
        {
            Email = "user@example.com",
            Token = "token",
            NewPassword = "Passw0rd!",
            ConfirmPassword = "Passw0rd!"
        };

        var result = _validator.TestValidate(cmd);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenPasswordConfirmationMismatches_ShouldFail()
    {
        var cmd = new SetupPasswordCommand
        {
            Email = "user@example.com",
            Token = "token",
            NewPassword = "Passw0rd!",
            ConfirmPassword = "Different!"
        };

        var result = _validator.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.ConfirmPassword);
    }

    [Fact]
    public void Validate_WhenPasswordIsTooShort_ShouldFail()
    {
        var cmd = new SetupPasswordCommand
        {
            Email = "user@example.com",
            Token = "token",
            NewPassword = "short",
            ConfirmPassword = "short"
        };

        var result = _validator.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void Validate_WhenEmailIsInvalid_ShouldFail()
    {
        var cmd = new SetupPasswordCommand
        {
            Email = "not-an-email",
            Token = "token",
            NewPassword = "Passw0rd!",
            ConfirmPassword = "Passw0rd!"
        };

        var result = _validator.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }
}
