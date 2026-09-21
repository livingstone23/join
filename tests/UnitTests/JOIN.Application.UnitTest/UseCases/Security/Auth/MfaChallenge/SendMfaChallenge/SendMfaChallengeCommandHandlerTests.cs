using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Auth.MfaChallenge.SendMfaChallenge;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Auth.MfaChallenge.SendMfaChallenge;

/// <summary>
/// Contains the unit tests for <see cref="SendMfaChallengeCommandHandler"/>: method gating,
/// challenge lookup/expiration, the 60s resend cooldown, and the email dispatch path — SPEC 32 F6.
/// </summary>
public sealed class SendMfaChallengeCommandHandlerTests
{
    /// <summary>
    /// Verifies that requesting a resend for "totp" is rejected — only email is resendable.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMethodIsTotp_ShouldReturnMethodNotResendable()
    {
        var context = new SendMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand(method: "totp");

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CHALLENGE_METHOD_NOT_RESENDABLE");
        context.ChallengeRepositoryMock.Verify(
            x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when no active challenge matches the token hash.
    /// </summary>
    [Fact]
    public async Task Handle_WhenChallengeNotFound_ShouldReturnChallengeNotFound()
    {
        var context = new SendMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand();

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaLoginChallenge?)null);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CHALLENGE_NOT_FOUND");
    }

    /// <summary>
    /// Verifies the expired branch, distinct from not-found.
    /// </summary>
    [Fact]
    public async Task Handle_WhenChallengeExpired_ShouldReturnChallengeExpired()
    {
        var context = new SendMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand();
        var challenge = CreateChallenge(expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CHALLENGE_EXPIRED");
    }

    /// <summary>
    /// Verifies the fail-closed branch when the challenge's user can no longer be resolved.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserNotFound_ShouldReturnChallengeNotFound()
    {
        var context = new SendMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand();
        var challenge = CreateChallenge();

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(challenge.UserId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CHALLENGE_NOT_FOUND");
    }

    /// <summary>
    /// Verifies that email is rejected when the user no longer has it active (mid-challenge change).
    /// </summary>
    [Fact]
    public async Task Handle_WhenEmailOtpNotActiveForUser_ShouldReturnMethodNotAvailable()
    {
        var context = new SendMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand();
        var challenge = CreateChallenge();
        var user = CreateUser(challenge.UserId, isEmailOtpEnabled: false);

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(challenge.UserId.ToString()))
            .ReturnsAsync(user);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CHALLENGE_METHOD_NOT_AVAILABLE");
    }

    /// <summary>
    /// Verifies the 60s resend cooldown rejects a send that follows too soon after the last one.
    /// </summary>
    [Fact]
    public async Task Handle_WhenWithinCooldownWindow_ShouldReturnSendCooldown()
    {
        var context = new SendMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand();
        var challenge = CreateChallenge(emailSentAtUtc: DateTime.UtcNow.AddSeconds(-10));
        var user = CreateUser(challenge.UserId, isEmailOtpEnabled: true);

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(challenge.UserId.ToString()))
            .ReturnsAsync(user);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CHALLENGE_SEND_COOLDOWN");
        context.ChallengeRepositoryMock.Verify(
            x => x.UpdateEmailCodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: a code is minted, stored tied to the challenge's own expiration,
    /// dispatched by email, and the audit event fires.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoCooldownAndEmailActive_ShouldStoreCodeAndDispatchEmail()
    {
        var context = new SendMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand();
        var challenge = CreateChallenge(emailSentAtUtc: null);
        var user = CreateUser(challenge.UserId, isEmailOtpEnabled: true);

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(challenge.UserId.ToString()))
            .ReturnsAsync(user);

        context.EmailServiceMock
            .Setup(x => x.SendEmailAsync(user.Email!, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();

        context.ChallengeRepositoryMock.Verify(
            x => x.UpdateEmailCodeAsync(challenge.Id, It.IsAny<string>(), challenge.ExpiresAtUtc, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);

        context.EmailServiceMock.Verify(
            x => x.SendEmailAsync(user.Email!, It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);

        context.SecurityEventLoggerMock.Verify(
            x => x.LogAsync(
                SecurityEventType.LoginMfaChallengeCodeSent,
                SecurityEventResult.Success,
                user.Id,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a delivery failure still surfaces as an unsuccessful response, mirroring
    /// <c>RequestPhoneVerificationCommandHandler</c>'s pattern.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEmailDispatchFails_ShouldReturnUnsuccessfulResponse()
    {
        var context = new SendMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand();
        var challenge = CreateChallenge(emailSentAtUtc: null);
        var user = CreateUser(challenge.UserId, isEmailOtpEnabled: true);

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(challenge.UserId.ToString()))
            .ReturnsAsync(user);

        context.EmailServiceMock
            .Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
    }

    private static SendMfaChallengeCommand CreateCommand(string method = "email")
    {
        return new SendMfaChallengeCommand
        {
            ChallengeToken = "opaque-token",
            Method = method
        };
    }

    private static MfaLoginChallenge CreateChallenge(DateTime? expiresAtUtc = null, DateTime? emailSentAtUtc = null)
    {
        return new MfaLoginChallenge(Guid.NewGuid())
        {
            UserId = Guid.NewGuid(),
            ExpiresAtUtc = expiresAtUtc ?? DateTime.UtcNow.AddMinutes(5),
            EmailSentAtUtc = emailSentAtUtc,
            AttemptCount = 0
        };
    }

    private static ApplicationUser CreateUser(Guid id, bool isEmailOtpEnabled)
    {
        return new ApplicationUser
        {
            Id = id,
            Email = "user@join.com",
            IsActive = true,
            IsEmailOtpEnabled = isEmailOtpEnabled,
            GcRecord = 0
        };
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();

        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
    }

    private sealed class SendMfaChallengeCommandHandlerTestContext
    {
        public SendMfaChallengeCommandHandlerTestContext()
        {
            CurrentUserServiceMock.SetupGet(x => x.IpAddress).Returns("127.0.0.1");
            CurrentUserServiceMock.SetupGet(x => x.UserAgent).Returns("unit-test");
        }

        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } = CreateUserManagerMock();
        public Mock<IMfaLoginChallengeRepository> ChallengeRepositoryMock { get; } = new();
        public Mock<IEmailService> EmailServiceMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();

        public SendMfaChallengeCommandHandler CreateHandler()
        {
            return new SendMfaChallengeCommandHandler(
                UserManagerMock.Object,
                ChallengeRepositoryMock.Object,
                EmailServiceMock.Object,
                CurrentUserServiceMock.Object,
                SecurityEventLoggerMock.Object);
        }
    }
}
