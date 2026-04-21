using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Common.CommunicationChannels.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.CommunicationChannels.Commands.UpdateCommunicationChannel;

/// <summary>
/// Contains unit tests for <see cref="UpdateCommunicationChannelCommandValidator"/>.
/// </summary>
public sealed class UpdateCommunicationChannelCommandValidatorTests
{
    private readonly UpdateCommunicationChannelCommandValidator _validator = new();

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
        result.ShouldNotHaveValidationErrorFor(x => x.Provider);
        result.ShouldNotHaveValidationErrorFor(x => x.Code);
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
            .WithErrorMessage("Communication channel id is required.");
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
            .WithErrorMessage("Channel name is required.");
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
            .WithErrorMessage("Channel name is required.");
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
            .WithErrorMessage("Channel name cannot exceed 100 characters.");
    }

    /// <summary>
    /// Verifies that a provider longer than 100 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenProviderExceeds100Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Provider = new string('P', 101) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Provider)
            .WithErrorMessage("Provider cannot exceed 100 characters.");
    }

    /// <summary>
    /// Verifies that a null provider skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenProviderIsNull_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Provider = null };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Provider);
    }

    /// <summary>
    /// Verifies that an empty provider skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenProviderIsEmpty_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Provider = string.Empty };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Provider);
    }

    /// <summary>
    /// Verifies that a whitespace-only provider skips the conditional max-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenProviderIsWhitespace_ShouldNotHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Provider = "   " };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Provider);
    }

    /// <summary>
    /// Verifies that a code longer than 50 characters triggers the max-length validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenCodeExceeds50Characters_ShouldHaveValidationError()
    {
        // Arrange
        var command = CreateValidCommand() with { Code = new string('C', 51) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Code)
            .WithErrorMessage("Code cannot exceed 50 characters.");
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

    private static UpdateCommunicationChannelCommand CreateValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        Name = "WhatsApp",
        Provider = "Twilio",
        Code = "WA"
    };
}