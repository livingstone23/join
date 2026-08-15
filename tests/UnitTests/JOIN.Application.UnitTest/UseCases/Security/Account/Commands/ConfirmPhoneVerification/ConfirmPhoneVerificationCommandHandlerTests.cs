using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Account.Commands.ConfirmPhoneVerification;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Commands.ConfirmPhoneVerification;

/// <summary>
/// Unit tests for the <c>POST /api/v1/account/verify-phone/confirm</c> handler
/// (SPEC 26 / F8).
/// </summary>
public sealed class ConfirmPhoneVerificationCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Happy path: a matching code verifies the user's phone and logs the audit event.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeMatches_ShouldConfirmPhoneAndLogConfirmed()
    {
        // Arrange
        var context = new ConfirmPhoneVerificationCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);
        var plaintext = "123456";
        var storedId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        var stored = new PhoneVerificationCode(storedId)
        {
            UserId = callerId,
            PhoneNumber = "+14155552671",
            CodeHash = RecoveryCodeHasher.Hash(plaintext),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            AttemptCount = 0
        };

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.UserManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        context.PhoneCodeRepositoryMock
            .Setup(x => x.GetLatestActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        context.PhoneCodeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(storedId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        context.PhoneCodeRepositoryMock
            .Setup(x => x.MarkUsedAsync(storedId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new ConfirmPhoneVerificationCommand(plaintext), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        user.PhoneNumberConfirmed.Should().BeTrue();
        user.PhoneNumber.Should().Be("+14155552671");

        VerifyEventLogged(context, callerId, SecurityEventType.PhoneVerificationConfirmed);
    }

    /// <summary>
    /// Invalid code: the verified hash doesn't match. Attempt counter increments, audit logs
    /// PhoneVerificationFailed, response is PHONE_CODE_INVALID.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeIsInvalid_ShouldReturnPhoneCodeInvalid()
    {
        // Arrange
        var context = new ConfirmPhoneVerificationCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);
        var storedId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        var stored = new PhoneVerificationCode(storedId)
        {
            UserId = callerId,
            PhoneNumber = "+14155552671",
            CodeHash = RecoveryCodeHasher.Hash("123456"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            AttemptCount = 0
        };

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.PhoneCodeRepositoryMock
            .Setup(x => x.GetLatestActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        context.PhoneCodeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(storedId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new ConfirmPhoneVerificationCommand("000000"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PHONE_CODE_INVALID");
        user.PhoneNumberConfirmed.Should().BeFalse();

        VerifyEventLogged(context, callerId, SecurityEventType.PhoneVerificationFailed);
    }

    /// <summary>
    /// Expired path: GetLatestActiveByUserAsync returns null. Handler returns PHONE_CODE_EXPIRED.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoActiveCodeExists_ShouldReturnPhoneCodeExpired()
    {
        // Arrange
        var context = new ConfirmPhoneVerificationCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.PhoneCodeRepositoryMock
            .Setup(x => x.GetLatestActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PhoneVerificationCode?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new ConfirmPhoneVerificationCommand("123456"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PHONE_CODE_EXPIRED");

        context.PhoneCodeRepositoryMock.Verify(
            x => x.IncrementAttemptAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Lock after 5 failed attempts. Increment returns 6 (&gt;5) → handler invalidates the row
    /// and returns PHONE_CODE_LOCKED + PhoneVerificationLocked audit event.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAttemptCountExceedsThreshold_ShouldReturnPhoneCodeLocked()
    {
        // Arrange
        var context = new ConfirmPhoneVerificationCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);
        var storedId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        var stored = new PhoneVerificationCode(storedId)
        {
            UserId = callerId,
            PhoneNumber = "+14155552671",
            CodeHash = RecoveryCodeHasher.Hash("654321"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            AttemptCount = 5
        };

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.PhoneCodeRepositoryMock
            .Setup(x => x.GetLatestActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        context.PhoneCodeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(storedId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(6);
        context.PhoneCodeRepositoryMock
            .Setup(x => x.InvalidateActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new ConfirmPhoneVerificationCommand("000000"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PHONE_CODE_LOCKED");

        context.PhoneCodeRepositoryMock.Verify(
            x => x.InvalidateActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);

        VerifyEventLogged(context, callerId, SecurityEventType.PhoneVerificationLocked);
    }

    /// <summary>
    /// Code already used by another call: handler returns PHONE_CODE_EXPIRED even when the
    /// supplied code matches the stored hash.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStoredCodeIsAlreadyConsumed_ShouldReturnPhoneCodeExpired()
    {
        // Arrange
        var context = new ConfirmPhoneVerificationCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);
        var plaintext = "987654";
        var storedId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        var stored = new PhoneVerificationCode(storedId)
        {
            UserId = callerId,
            PhoneNumber = "+14155552671",
            CodeHash = RecoveryCodeHasher.Hash(plaintext),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            AttemptCount = 1
        };

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.PhoneCodeRepositoryMock
            .Setup(x => x.GetLatestActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        context.PhoneCodeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(storedId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        // MarkUsedAsync returns false → race lost → expired-equivalent.
        context.PhoneCodeRepositoryMock
            .Setup(x => x.MarkUsedAsync(storedId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new ConfirmPhoneVerificationCommand(plaintext), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PHONE_CODE_EXPIRED");
        user.PhoneNumberConfirmed.Should().BeFalse();

        context.UserManagerMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    private static ApplicationUser CreateUser(Guid userId)
    {
        return new ApplicationUser
        {
            Id = userId,
            UserName = $"join.user{userId:N}",
            Email = "user@join.com",
            IsActive = true,
            GcRecord = 0
        };
    }

    private static void VerifyEventLogged(
        ConfirmPhoneVerificationCommandHandlerTestContext context,
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
    /// Self-contained mocks for ConfirmPhoneVerification dependencies.
    /// </summary>
    private sealed class ConfirmPhoneVerificationCommandHandlerTestContext
    {
        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } = CreateUserManagerMock();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IPhoneVerificationCodeRepository> PhoneCodeRepositoryMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();

        public void SetUser(Guid userId)
        {
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.IpAddress).Returns("127.0.0.1");
            CurrentUserServiceMock.SetupGet(x => x.UserAgent).Returns("unit-test");
        }

        public ConfirmPhoneVerificationCommandHandler CreateHandler() =>
            new(
                UserManagerMock.Object,
                CurrentUserServiceMock.Object,
                PhoneCodeRepositoryMock.Object,
                SecurityEventLoggerMock.Object);

        private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(
                store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        }
    }
}
