using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Admin.CompanyModules.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.CompanyModules.Commands.DeleteCompanyModules;

/// <summary>
/// Contains unit tests for <see cref="DeleteCompanyModulesCommandValidator"/>.
/// </summary>
public sealed class DeleteCompanyModulesCommandValidatorTests
{
    private readonly DeleteCompanyModulesCommandValidator _validator = new();

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
        result.ShouldNotHaveValidationErrorFor(x => x.Id);
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

    private static DeleteCompanyModulesCommand CreateValidCommand() => new(Guid.NewGuid());
}