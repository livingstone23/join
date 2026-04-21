using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Common.Companies.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.Companies.Commands.DeleteCompany;

/// <summary>
/// Contains unit tests for <see cref="DeleteCompanyCommandValidator"/>.
/// </summary>
public sealed class DeleteCompanyCommandValidatorTests
{
    private readonly DeleteCompanyCommandValidator _validator = new();

    /// <summary>
    /// Verifies that a valid identifier passes all validation rules.
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
            .WithErrorMessage("Company id is required.");
    }

    private static DeleteCompanyCommand CreateValidCommand() => new(Guid.NewGuid());
}