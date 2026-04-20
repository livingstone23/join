using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Admin.CompanyModules.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.CompanyModules.Commands.CreateCompanyModules;

/// <summary>
/// Contains unit tests for <see cref="CreateCompanyModulesCommandValidator"/>.
/// </summary>
public sealed class CreateCompanyModulesCommandValidatorTests
{
    private readonly CreateCompanyModulesCommandValidator _validator = new();

    /// <summary>
    /// Verifies that a fully populated command passes all validation rules.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandIsValid_ShouldNotHaveValidationErrors()
    {
        // Arrange
        var command = CreateValidCommand();

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.CompanyId);
        result.ShouldNotHaveValidationErrorFor(x => x.ModuleId);
    }

    /// <summary>
    /// Verifies that an empty company identifier triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenCompanyIdIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { CompanyId = Guid.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.CompanyId)
            .WithErrorMessage("CompanyId is required.");
    }

    /// <summary>
    /// Verifies that an empty module identifier triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenModuleIdIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { ModuleId = Guid.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ModuleId)
            .WithErrorMessage("ModuleId is required.");
    }

    private static CreateCompanyModulesCommand CreateValidCommand() => new()
    {
        CompanyId = Guid.NewGuid(),
        ModuleId = Guid.NewGuid(),
        IsActive = true
    };
}