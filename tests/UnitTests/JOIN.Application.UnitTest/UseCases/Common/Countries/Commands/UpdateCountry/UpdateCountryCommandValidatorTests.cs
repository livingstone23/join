using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Common.Countries.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.Countries.Commands.UpdateCountry;

/// <summary>
/// Contains unit tests for <see cref="UpdateCountryCommandValidator"/>.
/// </summary>
public sealed class UpdateCountryCommandValidatorTests
{
    private readonly UpdateCountryCommandValidator _validator = new();

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
        result.ShouldNotHaveValidationErrorFor(x => x.IsoCode);
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
            .WithErrorMessage("Country name is required.");
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
            .WithErrorMessage("Country name is required.");
    }

    /// <summary>
    /// Verifies that a name longer than 100 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenNameExceeds100Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Name = new string('N', 101) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Country name cannot exceed 100 characters.");
    }

    /// <summary>
    /// Verifies that an empty ISO code triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenIsoCodeIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { IsoCode = string.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.IsoCode)
            .WithErrorMessage("ISO code is required.");
    }

    /// <summary>
    /// Verifies that a whitespace-only ISO code triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenIsoCodeIsWhitespace_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { IsoCode = "   " };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.IsoCode)
            .WithErrorMessage("ISO code is required.");
    }

    /// <summary>
    /// Verifies that an ISO code longer than 10 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenIsoCodeExceeds10Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { IsoCode = new string('I', 11) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.IsoCode)
            .WithErrorMessage("ISO code cannot exceed 10 characters.");
    }

    private static UpdateCountryCommand CreateValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Nicaragua",
        IsoCode = "NI"
    };
}