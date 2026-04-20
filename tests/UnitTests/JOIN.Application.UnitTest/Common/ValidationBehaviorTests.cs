using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using JOIN.Application.Common;
using MediatR;
using Moq;

namespace JOIN.Application.UnitTest.Common;

/// <summary>
/// Contains unit tests for <see cref="ValidationBehavior{TRequest, TResponse}"/>.
/// Each test exercises a single branch of the pipeline behavior logic.
/// </summary>
public sealed class ValidationBehaviorTests
{
    /// <summary>Minimal request stub for pipeline tests.</summary>
    public sealed record TestRequest(string Value) : IRequest<string>;

    // ──────────────────────────────────────────────
    //  No validators registered
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that when no validators are injected, the pipeline delegates directly to the next handler.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoValidatorsRegistered_ShouldCallNextAndReturnResponse()
    {
        // Arrange
        var behavior = new ValidationBehavior<TestRequest, string>([]);
        var nextCalled = false;

        // Act
        var response = await behavior.Handle(
            new TestRequest("hello"),
            _ => { nextCalled = true; return Task.FromResult("ok"); },
            CancellationToken.None);

        // Assert
        nextCalled.Should().BeTrue();
        response.Should().Be("ok");
    }

    // ──────────────────────────────────────────────
    //  Single validator — passes
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that when a single validator reports no failures, the next handler is called.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSingleValidatorPasses_ShouldCallNextAndReturnResponse()
    {
        // Arrange
        var validatorMock = new Mock<IValidator<TestRequest>>();
        validatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var behavior = new ValidationBehavior<TestRequest, string>([validatorMock.Object]);
        var nextCalled = false;

        // Act
        var response = await behavior.Handle(
            new TestRequest("hello"),
            _ => { nextCalled = true; return Task.FromResult("ok"); },
            CancellationToken.None);

        // Assert
        nextCalled.Should().BeTrue();
        response.Should().Be("ok");
    }

    // ──────────────────────────────────────────────
    //  Single validator — fails
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that a single validator failure throws <see cref="JOIN.Application.Common.ValidationException"/>
    /// and does not invoke the next handler.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSingleValidatorFails_ShouldThrowValidationExceptionAndNotCallNext()
    {
        // Arrange
        var failure = new ValidationFailure("Value", "Value must not be empty.");
        var validatorMock = new Mock<IValidator<TestRequest>>();
        validatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([failure]));

        var behavior = new ValidationBehavior<TestRequest, string>([validatorMock.Object]);
        var nextCalled = false;

        // Act
        var act = () => behavior.Handle(
            new TestRequest(string.Empty),
            _ => { nextCalled = true; return Task.FromResult("ok"); },
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<JOIN.Application.Common.ValidationException>();
        nextCalled.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the thrown exception carries the correct error grouped by property name.
    /// </summary>
    [Fact]
    public async Task Handle_WhenValidatorFails_ShouldThrowExceptionWithCorrectErrors()
    {
        // Arrange
        var failure = new ValidationFailure("Value", "Value must not be empty.");
        var validatorMock = new Mock<IValidator<TestRequest>>();
        validatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([failure]));

        var behavior = new ValidationBehavior<TestRequest, string>([validatorMock.Object]);

        // Act
        var act = () => behavior.Handle(
            new TestRequest(string.Empty),
            _ => Task.FromResult("ok"),
            CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<JOIN.Application.Common.ValidationException>();
        exception.Which.Message.Should().Be("One or more validation failures have occurred.");
        exception.Which.Errors.Should().ContainKey("Value");
        exception.Which.Errors["Value"].Should().Contain("Value must not be empty.");
    }

    // ──────────────────────────────────────────────
    //  Multiple validators — one fails
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that when multiple validators are registered and at least one fails,
    /// a <see cref="JOIN.Application.Common.ValidationException"/> is thrown.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMultipleValidatorsAndOneFails_ShouldThrowValidationException()
    {
        // Arrange
        var passValidatorMock = new Mock<IValidator<TestRequest>>();
        passValidatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var failure = new ValidationFailure("Value", "Value is invalid.");
        var failValidatorMock = new Mock<IValidator<TestRequest>>();
        failValidatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([failure]));

        var behavior = new ValidationBehavior<TestRequest, string>(
            [passValidatorMock.Object, failValidatorMock.Object]);

        // Act
        var act = () => behavior.Handle(
            new TestRequest("bad"),
            _ => Task.FromResult("ok"),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<JOIN.Application.Common.ValidationException>();
    }

    // ──────────────────────────────────────────────
    //  Multiple failures for the same property
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that multiple failures for the same property are grouped under the same key in the exception.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMultipleFailuresForSameProperty_ShouldGroupErrorsInException()
    {
        // Arrange
        var failures = new[]
        {
            new ValidationFailure("Value", "Error 1."),
            new ValidationFailure("Value", "Error 2.")
        };

        var validatorMock = new Mock<IValidator<TestRequest>>();
        validatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(failures));

        var behavior = new ValidationBehavior<TestRequest, string>([validatorMock.Object]);

        // Act
        var act = () => behavior.Handle(
            new TestRequest(string.Empty),
            _ => Task.FromResult("ok"),
            CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<JOIN.Application.Common.ValidationException>();
        exception.Which.Errors.Should().ContainKey("Value");
        exception.Which.Errors["Value"].Should().HaveCount(2)
            .And.Contain("Error 1.")
            .And.Contain("Error 2.");
    }
}
