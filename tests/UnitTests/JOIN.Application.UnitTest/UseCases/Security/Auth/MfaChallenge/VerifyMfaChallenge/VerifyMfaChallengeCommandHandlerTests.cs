using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Auth.MfaChallenge.VerifyMfaChallenge;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Auth.MfaChallenge.VerifyMfaChallenge;

/// <summary>
/// Contains the unit tests for <see cref="VerifyMfaChallengeCommandHandler"/>: method gating,
/// challenge lookup/expiration, TOTP and email code verification, the 5-attempt lockout that
/// invalidates the whole challenge, and the successful hand-off to
/// <see cref="IAuthenticatedSessionIssuer"/> — SPEC 32 F6.
/// </summary>
public sealed class VerifyMfaChallengeCommandHandlerTests
{
    /// <summary>
    /// Verifies that an unsupported method value is rejected before any repository lookup.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMethodIsUnsupported_ShouldReturnMethodNotAvailable()
    {
        var context = new VerifyMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand(method: "sms");

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CHALLENGE_METHOD_NOT_AVAILABLE");
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
        var context = new VerifyMfaChallengeCommandHandlerTestContext();
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
        var context = new VerifyMfaChallengeCommandHandlerTestContext();
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
        var context = new VerifyMfaChallengeCommandHandlerTestContext();
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
    /// Verifies that "totp" is rejected when the user no longer has it active (mid-challenge change).
    /// </summary>
    [Fact]
    public async Task Handle_WhenMethodNotActiveForUser_ShouldReturnMethodNotAvailable()
    {
        var context = new VerifyMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand(method: "totp");
        var challenge = CreateChallenge();
        var user = CreateUser(challenge.UserId, isMfaEnabled: false, isEmailOtpEnabled: true);

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
        context.ChallengeRepositoryMock.Verify(
            x => x.IncrementAttemptAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies the happy path for TOTP: correct code consumes the challenge and delegates to
    /// <see cref="IAuthenticatedSessionIssuer"/> with the challenge's own target company.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTotpCodeIsCorrect_ShouldConsumeChallengeAndIssueSession()
    {
        var context = new VerifyMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand(method: "totp", code: "123456");
        var targetCompanyId = Guid.NewGuid();
        var challenge = CreateChallenge(targetCompanyId: targetCompanyId);
        var user = CreateUser(challenge.UserId, isMfaEnabled: true, mfaSecretKey: "SECRET");
        var sessionResponse = new LoginResponse { UserId = user.Id, Token = "jwt-token" };

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(challenge.UserId.ToString()))
            .ReturnsAsync(user);

        context.ChallengeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(challenge.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        context.TotpValidatorMock
            .Setup(x => x.ValidateCode("SECRET", "123456", 1))
            .Returns(true);

        context.ChallengeRepositoryMock
            .Setup(x => x.MarkConsumedAsync(challenge.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        context.AuthenticatedSessionIssuerMock
            .Setup(x => x.IssueAsync(user, targetCompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionResponse);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeSameAs(sessionResponse);

        context.ChallengeRepositoryMock.Verify(x => x.MarkConsumedAsync(challenge.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        context.AuthenticatedSessionIssuerMock.Verify(x => x.IssueAsync(user, targetCompanyId, It.IsAny<CancellationToken>()), Times.Once);
        context.ChallengeRepositoryMock.Verify(x => x.InvalidateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies an incorrect TOTP code fails without consuming the challenge or issuing a session.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTotpCodeIsIncorrect_ShouldReturnInvalidCodeWithoutIssuingSession()
    {
        var context = new VerifyMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand(method: "totp", code: "000000");
        var challenge = CreateChallenge();
        var user = CreateUser(challenge.UserId, isMfaEnabled: true, mfaSecretKey: "SECRET");

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(challenge.UserId.ToString()))
            .ReturnsAsync(user);

        context.ChallengeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(challenge.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        context.TotpValidatorMock
            .Setup(x => x.ValidateCode("SECRET", "000000", 1))
            .Returns(false);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CHALLENGE_INVALID_CODE");

        context.AuthenticatedSessionIssuerMock.Verify(
            x => x.IssueAsync(It.IsAny<ApplicationUser>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.ChallengeRepositoryMock.Verify(
            x => x.MarkConsumedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies the happy path for email: the code is checked against the challenge's own
    /// PBKDF2 hash (not the user, unlike TOTP).
    /// </summary>
    [Fact]
    public async Task Handle_WhenEmailCodeIsCorrect_ShouldConsumeChallengeAndIssueSession()
    {
        var context = new VerifyMfaChallengeCommandHandlerTestContext();
        const string plainCode = "654321";
        var command = CreateCommand(method: "email", code: plainCode);
        var challenge = CreateChallenge(
            emailCodeHash: RecoveryCodeHasher.Hash(plainCode),
            emailCodeExpiresAtUtc: DateTime.UtcNow.AddMinutes(5));
        var user = CreateUser(challenge.UserId, isEmailOtpEnabled: true);
        var sessionResponse = new LoginResponse { UserId = user.Id, Token = "jwt-token" };

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(challenge.UserId.ToString()))
            .ReturnsAsync(user);

        context.ChallengeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(challenge.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        context.ChallengeRepositoryMock
            .Setup(x => x.MarkConsumedAsync(challenge.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        context.AuthenticatedSessionIssuerMock
            .Setup(x => x.IssueAsync(user, challenge.TargetCompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionResponse);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeSameAs(sessionResponse);
    }

    /// <summary>
    /// Verifies that an email code past its own expiration fails even though the overall
    /// challenge is still active.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEmailCodeExpired_ShouldReturnInvalidCode()
    {
        var context = new VerifyMfaChallengeCommandHandlerTestContext();
        const string plainCode = "654321";
        var command = CreateCommand(method: "email", code: plainCode);
        var challenge = CreateChallenge(
            emailCodeHash: RecoveryCodeHasher.Hash(plainCode),
            emailCodeExpiresAtUtc: DateTime.UtcNow.AddMinutes(-1));
        var user = CreateUser(challenge.UserId, isEmailOtpEnabled: true);

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(challenge.UserId.ToString()))
            .ReturnsAsync(user);

        context.ChallengeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(challenge.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CHALLENGE_INVALID_CODE");
    }

    /// <summary>
    /// Verifies that no email code having been sent yet fails closed as an invalid code.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoEmailCodeWasEverSent_ShouldReturnInvalidCode()
    {
        var context = new VerifyMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand(method: "email", code: "123456");
        var challenge = CreateChallenge(emailCodeHash: null, emailCodeExpiresAtUtc: null);
        var user = CreateUser(challenge.UserId, isEmailOtpEnabled: true);

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(challenge.UserId.ToString()))
            .ReturnsAsync(user);

        context.ChallengeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(challenge.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CHALLENGE_INVALID_CODE");
    }

    /// <summary>
    /// Verifies the 5-failure lockout invalidates the WHOLE challenge (both methods), not just
    /// the method that was being attempted — same criterion as ConfirmPhoneVerificationCommandHandler.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAttemptsExceedFive_ShouldInvalidateEntireChallengeAndReturnLocked()
    {
        var context = new VerifyMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand(method: "totp", code: "999999");
        var challenge = CreateChallenge();
        var user = CreateUser(challenge.UserId, isMfaEnabled: true, mfaSecretKey: "SECRET");

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(challenge.UserId.ToString()))
            .ReturnsAsync(user);

        // 6th call: already over the 5-attempt threshold.
        context.ChallengeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(challenge.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(6);

        // Even a CORRECT code must fail once locked — the lock check runs before correctness.
        context.TotpValidatorMock
            .Setup(x => x.ValidateCode(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns(true);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CHALLENGE_LOCKED");

        context.ChallengeRepositoryMock.Verify(x => x.InvalidateAsync(challenge.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        context.AuthenticatedSessionIssuerMock.Verify(
            x => x.IssueAsync(It.IsAny<ApplicationUser>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.TotpValidatorMock.Verify(
            x => x.ValidateCode(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies a race where the challenge is consumed/invalidated between the correctness
    /// check and the mark-consumed write surfaces as expired rather than issuing a session.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMarkConsumedRaces_ShouldReturnChallengeExpired()
    {
        var context = new VerifyMfaChallengeCommandHandlerTestContext();
        var command = CreateCommand(method: "totp", code: "123456");
        var challenge = CreateChallenge();
        var user = CreateUser(challenge.UserId, isMfaEnabled: true, mfaSecretKey: "SECRET");

        context.ChallengeRepositoryMock
            .Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(challenge.UserId.ToString()))
            .ReturnsAsync(user);

        context.ChallengeRepositoryMock
            .Setup(x => x.IncrementAttemptAsync(challenge.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        context.TotpValidatorMock
            .Setup(x => x.ValidateCode("SECRET", "123456", 1))
            .Returns(true);

        context.ChallengeRepositoryMock
            .Setup(x => x.MarkConsumedAsync(challenge.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = context.CreateHandler();

        var response = await handler.Handle(command, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CHALLENGE_EXPIRED");
        context.AuthenticatedSessionIssuerMock.Verify(
            x => x.IssueAsync(It.IsAny<ApplicationUser>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static VerifyMfaChallengeCommand CreateCommand(string method = "totp", string code = "123456")
    {
        return new VerifyMfaChallengeCommand
        {
            ChallengeToken = "opaque-token",
            Method = method,
            Code = code
        };
    }

    private static MfaLoginChallenge CreateChallenge(
        DateTime? expiresAtUtc = null,
        Guid? targetCompanyId = null,
        string? emailCodeHash = null,
        DateTime? emailCodeExpiresAtUtc = null)
    {
        return new MfaLoginChallenge(Guid.NewGuid())
        {
            UserId = Guid.NewGuid(),
            TargetCompanyId = targetCompanyId,
            ExpiresAtUtc = expiresAtUtc ?? DateTime.UtcNow.AddMinutes(5),
            EmailCodeHash = emailCodeHash,
            EmailCodeExpiresAtUtc = emailCodeExpiresAtUtc,
            AttemptCount = 0
        };
    }

    private static ApplicationUser CreateUser(
        Guid id,
        bool isMfaEnabled = false,
        bool isEmailOtpEnabled = false,
        string? mfaSecretKey = null)
    {
        return new ApplicationUser
        {
            Id = id,
            Email = "user@join.com",
            IsActive = true,
            IsMfaEnabled = isMfaEnabled,
            IsEmailOtpEnabled = isEmailOtpEnabled,
            MfaSecretKey = mfaSecretKey,
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

    private sealed class VerifyMfaChallengeCommandHandlerTestContext
    {
        public VerifyMfaChallengeCommandHandlerTestContext()
        {
            CurrentUserServiceMock.SetupGet(x => x.IpAddress).Returns("127.0.0.1");
            CurrentUserServiceMock.SetupGet(x => x.UserAgent).Returns("unit-test");
        }

        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } = CreateUserManagerMock();
        public Mock<IMfaLoginChallengeRepository> ChallengeRepositoryMock { get; } = new();
        public Mock<IMfaTotpValidator> TotpValidatorMock { get; } = new();
        public Mock<IAuthenticatedSessionIssuer> AuthenticatedSessionIssuerMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();

        public VerifyMfaChallengeCommandHandler CreateHandler()
        {
            return new VerifyMfaChallengeCommandHandler(
                UserManagerMock.Object,
                ChallengeRepositoryMock.Object,
                TotpValidatorMock.Object,
                AuthenticatedSessionIssuerMock.Object,
                CurrentUserServiceMock.Object,
                SecurityEventLoggerMock.Object);
        }
    }
}
