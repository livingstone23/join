using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Common.StreetTypes.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.StreetTypes.Commands.CreateStreetType;

/// <summary>
/// Contains unit tests for <see cref="CreateStreetTypeCommandValidator"/>.
/// </summary>
public sealed class CreateStreetTypeCommandValidatorTests
{
    private readonly CreateStreetTypeCommandValidator _validator = new();

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
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
        result.ShouldNotHaveValidationErrorFor(x => x.Abbreviation);
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
            .WithErrorMessage("Street type name is required.");
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
            .WithErrorMessage("Street type name is required.");
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
            .WithErrorMessage("Street type name cannot exceed 50 characters.");
    }

    /// <summary>
    /// Verifies that an empty abbreviation triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenAbbreviationIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Abbreviation = string.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Abbreviation)
            .WithErrorMessage("Street type abbreviation is required.");
    }

    /// <summary>
    /// Verifies that a whitespace-only abbreviation triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenAbbreviationIsWhitespace_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Abbreviation = "   " };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Abbreviation)
            .WithErrorMessage("Street type abbreviation is required.");
    }

    /// <summary>
    /// Verifies that an abbreviation longer than 10 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenAbbreviationExceeds10Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Abbreviation = new string('A', 11) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Abbreviation)
            .WithErrorMessage("Street type abbreviation cannot exceed 10 characters.");
    }

    private static CreateStreetTypeCommand CreateValidCommand() => new()
    {
        Name = "Avenue",
        Abbreviation = "Ave"
    };
}