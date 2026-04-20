using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.IdentificationTypes.Commands.DeleteIdentificationType;

/// <summary>
/// Contains unit tests for <see cref="DeleteIdentificationTypeCommandValidator"/>.
/// </summary>
public sealed class DeleteIdentificationTypeCommandValidatorTests
{
    private readonly DeleteIdentificationTypeCommandValidator _validator = new();

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
    /// Verifies that an empty identifier triggers the expected validation error.
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
            .WithErrorMessage("Identification type id is required.");
    }

    private static DeleteIdentificationTypeCommand CreateValidCommand() => new(Guid.NewGuid());
}