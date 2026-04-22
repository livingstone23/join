using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Common.Municipalities.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.Municipalities.Commands.UpdateMunicipality;

/// <summary>
/// Contains unit tests for <see cref="UpdateMunicipalityCommandValidator"/>.
/// </summary>
public sealed class UpdateMunicipalityCommandValidatorTests
{
    private readonly UpdateMunicipalityCommandValidator _validator = new();

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
        result.ShouldNotHaveValidationErrorFor(x => x.ProvinceId);
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
            .WithErrorMessage("Municipality id is required.");
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
            .WithErrorMessage("Municipality name is required.");
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
            .WithErrorMessage("Municipality name is required.");
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
            .WithErrorMessage("Municipality name cannot exceed 100 characters.");
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
            .WithErrorMessage("Municipality code cannot exceed 20 characters.");
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
    /// Verifies that an empty province identifier triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenProvinceIdIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { ProvinceId = Guid.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ProvinceId)
            .WithErrorMessage("ProvinceId is required.");
    }

    private static UpdateMunicipalityCommand CreateValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Ciudad Sandino",
        Code = "CS",
        ProvinceId = Guid.NewGuid()
    };
}