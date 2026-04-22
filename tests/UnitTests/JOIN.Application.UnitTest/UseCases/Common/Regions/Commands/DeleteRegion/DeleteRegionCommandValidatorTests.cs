using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Common.Regions.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.Regions.Commands.DeleteRegion;

/// <summary>
/// Contains unit tests for <see cref="DeleteRegionCommandValidator"/>.
/// </summary>
public sealed class DeleteRegionCommandValidatorTests
{
    private readonly DeleteRegionCommandValidator _validator = new();

    /// <summary>
    /// Verifies that a command with a valid identifier passes validation.
    /// </summary>
    [Fact]
    public void Validate_WhenIdIsValid_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = new DeleteRegionCommand(Guid.NewGuid());

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
        var command = new DeleteRegionCommand(Guid.Empty);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Region id is required.");
    }
}