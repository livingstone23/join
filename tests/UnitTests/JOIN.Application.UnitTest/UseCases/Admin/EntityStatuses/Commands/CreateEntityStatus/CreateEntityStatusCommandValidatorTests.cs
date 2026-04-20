using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Admin.EntityStatuses.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.EntityStatuses.Commands.CreateEntityStatus;

/// <summary>
/// Contains unit tests for <see cref="CreateEntityStatusCommandValidator"/>.
/// </summary>
public sealed class CreateEntityStatusCommandValidatorTests
{
    private readonly CreateEntityStatusCommandValidator _validator = new();

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
        result.ShouldNotHaveValidationErrorFor(x => x.CompanyId);
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
        result.ShouldNotHaveValidationErrorFor(x => x.Description);
        result.ShouldNotHaveValidationErrorFor(x => x.Code);
    }

    /// <summary>
    /// Verifies that an empty company identifier triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenCompanyIdIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { CompanyId = Guid.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.CompanyId)
            .WithErrorMessage("CompanyId is required.");
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
    /// Verifies that a code less than or equal to zero triggers the greater-than validation error.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WhenCodeIsNotGreaterThanZero_ShouldHaveValidationError(int code)
    {
        // Arrange
        var command = CreateValidCommand() with { Code = code };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Code)
            .WithErrorMessage("Code must be greater than 0.");
    }

    private static CreateEntityStatusCommand CreateValidCommand() => new()
    {
        CompanyId = Guid.NewGuid(),
        Name = "Active",
        Description = "Operational status",
        Code = 1,
        IsOperative = true
    };
}