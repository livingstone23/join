using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Admin.CompanyModules.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.CompanyModules.Commands.UpdateCompanyModules;

/// <summary>
/// Contains unit tests for <see cref="UpdateCompanyModulesCommandValidator"/>.
/// </summary>
public sealed class UpdateCompanyModulesCommandValidatorTests
{
    private readonly UpdateCompanyModulesCommandValidator _validator = new();

    /// <summary>
    /// Verifies that a fully populated command passes all existing validation rules.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandIsValid_ShouldNotHaveValidationErrors()
    {
        // Arrange
        var command = CreateValidCommand();

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Id);
        result.ShouldNotHaveValidationErrorFor(x => x.CompanyId);
    }

    /// <summary>
    /// Verifies that an empty identifier triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenIdIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Id = Guid.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Id is required.");
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

    private static UpdateCompanyModulesCommand CreateValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        IsActive = true
    };
}