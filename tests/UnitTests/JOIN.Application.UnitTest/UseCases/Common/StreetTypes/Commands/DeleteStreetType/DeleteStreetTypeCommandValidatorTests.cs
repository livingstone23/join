using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Common.StreetTypes.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.StreetTypes.Commands.DeleteStreetType;

/// <summary>
/// Contains unit tests for <see cref="DeleteStreetTypeCommandValidator"/>.
/// </summary>
public sealed class DeleteStreetTypeCommandValidatorTests
{
    private readonly DeleteStreetTypeCommandValidator _validator = new();

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
            .WithErrorMessage("Street type id is required.");
    }

    private static DeleteStreetTypeCommand CreateValidCommand() => new(Guid.NewGuid());
}