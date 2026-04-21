using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Common.Countries.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.Countries.Commands.DeleteCountry;

/// <summary>
/// Contains unit tests for <see cref="DeleteCountryCommandValidator"/>.
/// </summary>
public sealed class DeleteCountryCommandValidatorTests
{
    private readonly DeleteCountryCommandValidator _validator = new();

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
            .WithErrorMessage("Country id is required.");
    }

    private static DeleteCountryCommand CreateValidCommand() => new(Guid.NewGuid());
}