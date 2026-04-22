using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Common.Municipalities.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.Municipalities.Commands.DeleteMunicipality;

/// <summary>
/// Contains unit tests for <see cref="DeleteMunicipalityCommandValidator"/>.
/// </summary>
public sealed class DeleteMunicipalityCommandValidatorTests
{
    private readonly DeleteMunicipalityCommandValidator _validator = new();

    /// <summary>
    /// Verifies that a command with a valid identifier passes validation.
    /// </summary>
    [Fact]
    public void Validate_WhenIdIsValid_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = new DeleteMunicipalityCommand(Guid.NewGuid());

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
        var command = new DeleteMunicipalityCommand(Guid.Empty);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Municipality id is required.");
    }
}