using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Common.Companies.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.Companies.Commands.UpdateCompany;

/// <summary>
/// Contains unit tests for <see cref="UpdateCompanyCommandValidator"/>.
/// </summary>
public sealed class UpdateCompanyCommandValidatorTests
{
    private readonly UpdateCompanyCommandValidator _validator = new();

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
        result.ShouldNotHaveValidationErrorFor(x => x.TaxId);
        result.ShouldNotHaveValidationErrorFor(x => x.Email);
        result.ShouldNotHaveValidationErrorFor(x => x.Phone);
        result.ShouldNotHaveValidationErrorFor(x => x.WebSite);
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
            .WithErrorMessage("Company id is required.");
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
            .WithErrorMessage("Company name is required.");
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
            .WithErrorMessage("Company name is required.");
    }

    /// <summary>
    /// Verifies that a name longer than 150 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenNameExceeds150Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Name = new string('N', 151) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Company name cannot exceed 150 characters.");
    }

    /// <summary>
    /// Verifies that a description longer than 500 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenDescriptionExceeds500Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Description = new string('D', 501) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Description)
            .WithErrorMessage("Description cannot exceed 500 characters.");
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
    /// Verifies that an empty tax identifier triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenTaxIdIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { TaxId = string.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TaxId)
            .WithErrorMessage("Tax identifier is required.");
    }

    /// <summary>
    /// Verifies that a whitespace-only tax identifier triggers the required validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenTaxIdIsWhitespace_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { TaxId = "   " };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TaxId)
            .WithErrorMessage("Tax identifier is required.");
    }

    /// <summary>
    /// Verifies that a tax identifier longer than 50 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenTaxIdExceeds50Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { TaxId = new string('T', 51) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TaxId)
            .WithErrorMessage("Tax identifier cannot exceed 50 characters.");
    }

    /// <summary>
    /// Verifies that an email longer than 100 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenEmailExceeds100Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Email = new string('e', 101) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Email)
            .WithErrorMessage("Email cannot exceed 100 characters.");
    }

    /// <summary>
    /// Verifies that a null email skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenEmailIsNull_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Email = null };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Email);
    }

    /// <summary>
    /// Verifies that an empty email skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenEmailIsEmpty_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Email = string.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Email);
    }

    /// <summary>
    /// Verifies that a whitespace-only email skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenEmailIsWhitespace_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Email = "   " };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Email);
    }

    /// <summary>
    /// Verifies that a phone longer than 50 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenPhoneExceeds50Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Phone = new string('P', 51) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Phone)
            .WithErrorMessage("Phone cannot exceed 50 characters.");
    }

    /// <summary>
    /// Verifies that a null phone skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenPhoneIsNull_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Phone = null };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Phone);
    }

    /// <summary>
    /// Verifies that an empty phone skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenPhoneIsEmpty_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Phone = string.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Phone);
    }

    /// <summary>
    /// Verifies that a whitespace-only phone skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenPhoneIsWhitespace_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Phone = "   " };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Phone);
    }

    /// <summary>
    /// Verifies that a website longer than 200 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenWebSiteExceeds200Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { WebSite = new string('W', 201) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.WebSite)
            .WithErrorMessage("Website cannot exceed 200 characters.");
    }

    /// <summary>
    /// Verifies that a null website skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenWebSiteIsNull_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { WebSite = null };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.WebSite);
    }

    /// <summary>
    /// Verifies that an empty website skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenWebSiteIsEmpty_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { WebSite = string.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.WebSite);
    }

    /// <summary>
    /// Verifies that a whitespace-only website skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenWebSiteIsWhitespace_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { WebSite = "   " };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.WebSite);
    }

    private static UpdateCompanyCommand CreateValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        Name = "JOIN CRM",
        Description = "Tenant company",
        TaxId = "RUC-001",
        Email = "info@joincrm.com",
        Phone = "+50512345678",
        WebSite = "https://joincrm.com"
    };
}