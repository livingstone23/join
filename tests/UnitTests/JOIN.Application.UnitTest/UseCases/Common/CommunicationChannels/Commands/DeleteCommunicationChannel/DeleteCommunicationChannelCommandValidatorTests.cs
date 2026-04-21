using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Common.CommunicationChannels.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.CommunicationChannels.Commands.DeleteCommunicationChannel;

/// <summary>
/// Contains unit tests for <see cref="DeleteCommunicationChannelCommandValidator"/>.
/// </summary>
public sealed class DeleteCommunicationChannelCommandValidatorTests
{
    private readonly DeleteCommunicationChannelCommandValidator _validator = new();

    /// <summary>
    /// Verifies that a valid identifier passes all validation rules.
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

    private static DeleteCommunicationChannelCommand CreateValidCommand() => new(Guid.NewGuid());
}