using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Admin.Projects.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.Projects.Commands.CreateProject;

/// <summary>
/// Contains unit tests for <see cref="CreateProjectCommandValidator"/>.
/// </summary>
public sealed class CreateProjectCommandValidatorTests
{
    private readonly CreateProjectCommandValidator _validator = new();

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
        result.ShouldNotHaveValidationErrorFor(x => x.EntityStatusId);
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
    /// Verifies that a name longer than 150 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenNameExceeds150Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Name = new string('P', 151) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Project name is required and must not exceed 150 characters.");
    }

    /// <summary>
    /// Verifies that an empty entity status identifier triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenEntityStatusIdIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { EntityStatusId = Guid.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.EntityStatusId)
            .WithErrorMessage("EntityStatusId is required.");
    }

    private static CreateProjectCommand CreateValidCommand() => new()
    {
        CompanyId = Guid.NewGuid(),
        Name = "CRM Rollout",
        EntityStatusId = Guid.NewGuid()
    };
}