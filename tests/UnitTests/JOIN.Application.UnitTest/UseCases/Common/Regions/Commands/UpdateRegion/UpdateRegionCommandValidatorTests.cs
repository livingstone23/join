using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Common.Regions.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.Regions.Commands.UpdateRegion;

/// <summary>
/// Contains unit tests for <see cref="UpdateRegionCommandValidator"/>.
/// </summary>
public sealed class UpdateRegionCommandValidatorTests
{
    private readonly UpdateRegionCommandValidator _validator = new();

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
        result.ShouldNotHaveValidationErrorFor(x => x.Code);
        result.ShouldNotHaveValidationErrorFor(x => x.CountryId);
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
            .WithErrorMessage("Region id is required.");
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
            .WithErrorMessage("Region name is required.");
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
            .WithErrorMessage("Region name is required.");
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
            .WithErrorMessage("Region name cannot exceed 100 characters.");
    }

    /// <summary>
    /// Verifies that a code longer than 20 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenCodeExceeds20Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Code = new string('C', 21) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Code)
            .WithErrorMessage("Region code cannot exceed 20 characters.");
    }

    /// <summary>
    /// Verifies that a null code skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenCodeIsNull_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Code = null };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Code);
    }

    /// <summary>
    /// Verifies that an empty code skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenCodeIsEmpty_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Code = string.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Code);
    }

    /// <summary>
    /// Verifies that a whitespace-only code skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenCodeIsWhitespace_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Code = "   " };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Code);
    }

    /// <summary>
    /// Verifies that an empty country identifier triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenCountryIdIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { CountryId = Guid.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.CountryId)
            .WithErrorMessage("CountryId is required.");
    }

    private static UpdateRegionCommand CreateValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Pacific",
        Code = "PAC",
        CountryId = Guid.NewGuid()
    };
}