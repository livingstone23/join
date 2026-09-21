using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Account.Commands.EnableEmailOtp;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Commands.EnableEmailOtp;

/// <summary>
/// Unit tests for the <c>POST /api/v1/account/email-otp/enable</c> handler (SPEC 32 / F7).
/// </summary>
public sealed class EnableEmailOtpCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Happy path: a matching code flips <c>IsEmailOtpEnabled</c> and logs the audit event.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeMatches_ShouldEnableEmailOtp()
    {
        var context = new EnableEmailOtpCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);
        var plaintext = "123456";
        var storedId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        var stored = new EmailOtpEnableCode(storedId)
        {
            UserId = callerId,
            Email = "user@join.com",
            CodeHash = RecoveryCodeHasher.Hash(plaintext),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            AttemptCount = 0
        };

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.UserManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.GetLatestActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(storedId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.MarkUsedAsync(storedId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        var response = await handler.Handle(new EnableEmailOtpCommand(plaintext), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        user.IsEmailOtpEnabled.Should().BeTrue();

        context.SecurityEventLoggerMock.Verify(
            x => x.LogAsync(
                SecurityEventType.EmailOtpEnabled,
                SecurityEventResult.Success,
                callerId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Invalid code: attempt counter increments, response is EMAIL_OTP_CODE_INVALID.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeIsInvalid_ShouldReturnEmailOtpCodeInvalid()
    {
        var context = new EnableEmailOtpCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);
        var storedId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        var stored = new EmailOtpEnableCode(storedId)
        {
            UserId = callerId,
            Email = "user@join.com",
            CodeHash = RecoveryCodeHasher.Hash("123456"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            AttemptCount = 0
        };

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.GetLatestActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(storedId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        var response = await handler.Handle(new EnableEmailOtpCommand("000000"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_OTP_CODE_INVALID");
        user.IsEmailOtpEnabled.Should().BeFalse();
    }

    /// <summary>
    /// No code was ever requested: GetLatestActiveByUserAsync returns null.
    /// Distinct from expiration — EMAIL_OTP_NOT_REQUESTED, not EMAIL_OTP_CODE_EXPIRED.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoCodeWasEverRequested_ShouldReturnEmailOtpNotRequested()
    {
        var context = new EnableEmailOtpCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.GetLatestActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailOtpEnableCode?)null);

        var handler = context.CreateHandler();

        var response = await handler.Handle(new EnableEmailOtpCommand("123456"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_OTP_NOT_REQUESTED");

        context.EmailOtpCodeRepositoryMock.Verify(
            x => x.IncrementAttemptAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A code was requested but has since expired — distinct EMAIL_OTP_CODE_EXPIRED, checked
    /// before incrementing the attempt counter.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStoredCodeExpired_ShouldReturnEmailOtpCodeExpired()
    {
        var context = new EnableEmailOtpCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);
        var storedId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        var stored = new EmailOtpEnableCode(storedId)
        {
            UserId = callerId,
            Email = "user@join.com",
            CodeHash = RecoveryCodeHasher.Hash("123456"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1),
            AttemptCount = 0
        };

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.GetLatestActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var handler = context.CreateHandler();

        var response = await handler.Handle(new EnableEmailOtpCommand("123456"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_OTP_CODE_EXPIRED");

        context.EmailOtpCodeRepositoryMock.Verify(
            x => x.IncrementAttemptAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Lock after 5 failed attempts: increment returns 6 (&gt;5) → the whole active code set is
    /// invalidated and EMAIL_OTP_CODE_LOCKED is returned.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAttemptCountExceedsThreshold_ShouldReturnEmailOtpCodeLocked()
    {
        var context = new EnableEmailOtpCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);
        var storedId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        var stored = new EmailOtpEnableCode(storedId)
        {
            UserId = callerId,
            Email = "user@join.com",
            CodeHash = RecoveryCodeHasher.Hash("654321"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            AttemptCount = 5
        };

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.GetLatestActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(storedId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(6);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.InvalidateActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        var response = await handler.Handle(new EnableEmailOtpCommand("000000"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_OTP_CODE_LOCKED");

        context.EmailOtpCodeRepositoryMock.Verify(
            x => x.InvalidateActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Race on MarkUsedAsync: another call already consumed the code. Handler falls back to
    /// EMAIL_OTP_CODE_EXPIRED rather than double-flipping the flag.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMarkUsedRaces_ShouldReturnEmailOtpCodeExpired()
    {
        var context = new EnableEmailOtpCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);
        var plaintext = "987654";
        var storedId = _fixture.Create<Guid>();

        context.SetUser(callerId);

        var stored = new EmailOtpEnableCode(storedId)
        {
            UserId = callerId,
            Email = "user@join.com",
            CodeHash = RecoveryCodeHasher.Hash(plaintext),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            AttemptCount = 1
        };

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.GetLatestActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(storedId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.MarkUsedAsync(storedId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = context.CreateHandler();

        var response = await handler.Handle(new EnableEmailOtpCommand(plaintext), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_OTP_CODE_EXPIRED");
        user.IsEmailOtpEnabled.Should().BeFalse();

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

    private sealed class EnableEmailOtpCommandHandlerTestContext
    {
        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } = CreateUserManagerMock();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IEmailOtpEnableCodeRepository> EmailOtpCodeRepositoryMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();

        public void SetUser(Guid userId)
        {
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.IpAddress).Returns("127.0.0.1");
            CurrentUserServiceMock.SetupGet(x => x.UserAgent).Returns("unit-test");
        }

        public EnableEmailOtpCommandHandler CreateHandler() =>
            new(
                UserManagerMock.Object,
                CurrentUserServiceMock.Object,
                EmailOtpCodeRepositoryMock.Object,
                SecurityEventLoggerMock.Object);

        private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(
                store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        }
    }
}
