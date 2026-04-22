using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Common.Provinces.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.Provinces.Commands.DeleteProvince;

/// <summary>
/// Contains unit tests for <see cref="DeleteProvinceCommandValidator"/>.
/// </summary>
public sealed class DeleteProvinceCommandValidatorTests
{
    private readonly DeleteProvinceCommandValidator _validator = new();

    /// <summary>
    /// Verifies that a command with a valid identifier passes validation.
    /// </summary>
    [Fact]
    public void Validate_WhenIdIsValid_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = new DeleteProvinceCommand(Guid.NewGuid());

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
        var command = new DeleteProvinceCommand(Guid.Empty);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Province id is required.");
    }
}