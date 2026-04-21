using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.IdentificationTypes.Commands.UpdateIdentificationType;

/// <summary>
/// Contains unit tests for <see cref="UpdateIdentificationTypeCommandValidator"/>.
/// </summary>
public sealed class UpdateIdentificationTypeCommandValidatorTests
{
    private readonly UpdateIdentificationTypeCommandValidator _validator = new();

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
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
        result.ShouldNotHaveValidationErrorFor(x => x.Description);
        result.ShouldNotHaveValidationErrorFor(x => x.ValidationPattern);
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
    /// Verifies that an empty name triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenNameIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Name = string.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("'Name' must not be empty.");
    }

    /// <summary>
    /// Verifies that a whitespace-only name triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenNameIsWhitespace_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Name = "   " };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("'Name' must not be empty.");
    }

    /// <summary>
    /// Verifies that a name longer than 50 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenNameExceeds50Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Name = new string('N', 51) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Name is required and must not exceed 50 characters.");
    }

    /// <summary>
    /// Verifies that a description longer than 200 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenDescriptionExceeds200Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Description = new string('D', 201) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Description)
            .WithErrorMessage("Description must not exceed 200 characters.");
    }

    /// <summary>
    /// Verifies that a null description skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenDescriptionIsNull_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Description = null };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    /// <summary>
    /// Verifies that an empty description skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenDescriptionIsEmpty_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Description = string.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    /// <summary>
    /// Verifies that a whitespace-only description skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenDescriptionIsWhitespace_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Description = "   " };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    /// <summary>
    /// Verifies that a validation pattern longer than 200 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenValidationPatternExceeds200Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { ValidationPattern = new string('P', 201) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ValidationPattern)
            .WithErrorMessage("ValidationPattern must not exceed 200 characters.");
    }

    /// <summary>
    /// Verifies that a null validation pattern skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenValidationPatternIsNull_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { ValidationPattern = null };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.ValidationPattern);
    }

    /// <summary>
    /// Verifies that an empty validation pattern skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenValidationPatternIsEmpty_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { ValidationPattern = string.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.ValidationPattern);
    }

    /// <summary>
    /// Verifies that a whitespace-only validation pattern skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenValidationPatternIsWhitespace_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { ValidationPattern = "   " };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.ValidationPattern);
    }

    private static UpdateIdentificationTypeCommand CreateValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Passport",
        Description = "International identification document",
        ValidationPattern = "^[A-Z0-9]{6,20}$",
        IsActive = true
    };
}