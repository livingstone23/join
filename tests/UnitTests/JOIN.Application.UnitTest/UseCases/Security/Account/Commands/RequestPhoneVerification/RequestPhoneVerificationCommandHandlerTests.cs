using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Account.Commands.RequestPhoneVerification;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Commands.RequestPhoneVerification;

/// <summary>
/// Unit tests for the <c>POST /api/v1/account/verify-phone/request</c> handler
/// (SPEC 26 / F8).
/// </summary>
public sealed class RequestPhoneVerificationCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Happy path: an E.164 phone triggers invalidation + insert + SMS dispatch.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPhoneIsValidE164_ShouldInvalidateInsertAndSendSms()
    {
        // Arrange
        var context = new RequestPhoneVerificationCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        context.PhoneCodeRepositoryMock
            .Setup(x => x.InvalidateActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        PhoneVerificationCode? captured = null;
        context.PhoneCodeRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<PhoneVerificationCode>(), It.IsAny<CancellationToken>()))
            .Callback<PhoneVerificationCode, CancellationToken>((c, _) => captured = c)
            .Returns(Task.CompletedTask);

        context.SmsServiceMock
            .Setup(x => x.SendSmsAsync("+14155552671", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new RequestPhoneVerificationCommand("+14155552671"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.PhoneNumber.Should().Be("+14155552671");
        captured.UserId.Should().Be(callerId);
        captured.AttemptCount.Should().Be(0);
        captured.ExpiresAtUtc.Should().BeAfter(captured.Created);

        context.PhoneCodeRepositoryMock.Verify(
            x => x.InvalidateActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        context.PhoneCodeRepositoryMock.Verify(
            x => x.InsertAsync(It.IsAny<PhoneVerificationCode>(), It.IsAny<CancellationToken>()),
            Times.Once);
        context.SmsServiceMock.Verify(
            x => x.SendSmsAsync("+14155552671", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyEventLogged(context, callerId, SecurityEventType.PhoneVerificationRequested);
    }

    /// <summary>
    /// Invalid format: handler short-circuits with PHONE_INVALID_FORMAT and no SMS gets dispatched.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPhoneIsInvalid_ShouldReturnPhoneInvalidFormat()
    {
        // Arrange
        var context = new RequestPhoneVerificationCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new RequestPhoneVerificationCommand("not-a-phone"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PHONE_INVALID_FORMAT");

        context.PhoneCodeRepositoryMock.Verify(
            x => x.InsertAsync(It.IsAny<PhoneVerificationCode>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.SmsServiceMock.Verify(
            x => x.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Provider failure: SMS dispatch returns false. Handler logs the failure with
    /// SecurityEventResult.Failure and returns an unsuccessful response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSmsDispatchFails_ShouldLogFailureAndReturnUnsuccessful()
    {
        // Arrange
        var context = new RequestPhoneVerificationCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        context.PhoneCodeRepositoryMock
            .Setup(x => x.InvalidateActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        context.PhoneCodeRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<PhoneVerificationCode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        context.SmsServiceMock
            .Setup(x => x.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new RequestPhoneVerificationCommand("+14155552671"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Contain("Unable to send");

        context.SecurityEventLoggerMock.Verify(
            x => x.LogAsync(
                SecurityEventType.PhoneVerificationRequested,
                SecurityEventResult.Failure,
                callerId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static void VerifyEventLogged(
        RequestPhoneVerificationCommandHandlerTestContext context,
        Guid expectedUserId,
        SecurityEventType expectedType)
    {
        context.SecurityEventLoggerMock.Verify(
            x => x.LogAsync(
                expectedType,
                It.IsAny<SecurityEventResult>(),
                expectedUserId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Self-contained mocks for RequestPhoneVerification dependencies.
    /// </summary>
    private sealed class RequestPhoneVerificationCommandHandlerTestContext
    {
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IPhoneVerificationCodeRepository> PhoneCodeRepositoryMock { get; } = new();
        public Mock<ISmsService> SmsServiceMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();

        public void SetUser(Guid userId)
        {
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.IpAddress).Returns("127.0.0.1");
            CurrentUserServiceMock.SetupGet(x => x.UserAgent).Returns("unit-test");
        }

        public RequestPhoneVerificationCommandHandler CreateHandler() =>
            new(
                CurrentUserServiceMock.Object,
                PhoneCodeRepositoryMock.Object,
                SmsServiceMock.Object,
                SecurityEventLoggerMock.Object);
    }
}
