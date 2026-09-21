using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Account.Commands.DisableEmailOtp;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Commands.DisableEmailOtp;

/// <summary>
/// Unit tests for the <c>POST /api/v1/account/email-otp/disable</c> handler (SPEC 32 / F7).
/// </summary>
public sealed class DisableEmailOtpCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Happy path: a matching code flips <c>IsEmailOtpEnabled</c> off and logs the audit event.
    /// No minimum-active-method check — the only active method can be disabled.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeMatches_ShouldDisableEmailOtp()
    {
        var context = new DisableEmailOtpCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isEmailOtpEnabled: true);
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

        var response = await handler.Handle(new DisableEmailOtpCommand(plaintext), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        user.IsEmailOtpEnabled.Should().BeFalse();

        context.SecurityEventLoggerMock.Verify(
            x => x.LogAsync(
                SecurityEventType.EmailOtpDisabled,
                SecurityEventResult.Success,
                callerId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Invalid code: response is EMAIL_OTP_CODE_INVALID and the flag is untouched.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeIsInvalid_ShouldReturnEmailOtpCodeInvalid()
    {
        var context = new DisableEmailOtpCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isEmailOtpEnabled: true);
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

        var response = await handler.Handle(new DisableEmailOtpCommand("000000"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_OTP_CODE_INVALID");
        user.IsEmailOtpEnabled.Should().BeTrue();
    }

    /// <summary>
    /// No code was ever requested: distinct EMAIL_OTP_NOT_REQUESTED, not EMAIL_OTP_CODE_EXPIRED.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoCodeWasEverRequested_ShouldReturnEmailOtpNotRequested()
    {
        var context = new DisableEmailOtpCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isEmailOtpEnabled: true);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.GetLatestActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailOtpEnableCode?)null);

        var handler = context.CreateHandler();

        var response = await handler.Handle(new DisableEmailOtpCommand("123456"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_OTP_NOT_REQUESTED");
    }

    /// <summary>
    /// A code was requested but has since expired — distinct EMAIL_OTP_CODE_EXPIRED.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStoredCodeExpired_ShouldReturnEmailOtpCodeExpired()
    {
        var context = new DisableEmailOtpCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isEmailOtpEnabled: true);
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

        var response = await handler.Handle(new DisableEmailOtpCommand("123456"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_OTP_CODE_EXPIRED");
    }

    /// <summary>
    /// Lock after 5 failed attempts invalidates the active code set and returns
    /// EMAIL_OTP_CODE_LOCKED.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAttemptCountExceedsThreshold_ShouldReturnEmailOtpCodeLocked()
    {
        var context = new DisableEmailOtpCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isEmailOtpEnabled: true);
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

        var response = await handler.Handle(new DisableEmailOtpCommand("000000"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_OTP_CODE_LOCKED");
    }

    private static ApplicationUser CreateUser(Guid userId, bool isEmailOtpEnabled)
    {
        return new ApplicationUser
        {
            Id = userId,
            UserName = $"join.user{userId:N}",
            Email = "user@join.com",
            IsActive = true,
            IsEmailOtpEnabled = isEmailOtpEnabled,
            GcRecord = 0
        };
    }

    private sealed class DisableEmailOtpCommandHandlerTestContext
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

        public DisableEmailOtpCommandHandler CreateHandler() =>
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
