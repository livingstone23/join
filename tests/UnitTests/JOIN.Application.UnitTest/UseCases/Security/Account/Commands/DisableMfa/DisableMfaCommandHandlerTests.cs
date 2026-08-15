using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Account.Commands.DisableMfa;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Commands.DisableMfa;

/// <summary>
/// Unit tests for the <c>POST /api/v1/account/mfa/disable</c> handler (SPEC 26 / F6).
/// </summary>
public sealed class DisableMfaCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// TOTP path: a valid 6-digit code disables MFA, wipes the recovery-code set and logs MFA_DISABLED.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTotpCodeIsValid_ShouldDisableMfaAndLogMfaDisabled()
    {
        // Arrange
        var context = new DisableMfaCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isEnabled: true, hasSecret: true);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.UserManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        context.MfaTotpValidatorMock
            .Setup(x => x.ValidateCode(user.MfaSecretKey!, "123456"))
            .Returns(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DisableMfaCommand("123456"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        user.IsMfaEnabled.Should().BeFalse();
        user.MfaSecretKey.Should().BeNull();
        user.MfaEnabledAtUtc.Should().BeNull();

        context.RecoveryCodeRepositoryMock.Verify(
            x => x.DeleteAllByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyEventLogged(context, callerId, SecurityEventType.MfaDisabled);
    }

    /// <summary>
    /// Recovery-code path: the user supplies a recovery code that matches one of the stored
    /// PBKDF2 hashes. MFA is disabled and the row is marked used.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRecoveryCodeMatches_ShouldMarkUsedAndLogMfaRecoveryCodeUsed()
    {
        // Arrange
        var context = new DisableMfaCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isEnabled: true, hasSecret: true);
        var plaintextCode = RecoveryCodeHasher.GenerateCode();
        var storedHash = RecoveryCodeHasher.Hash(plaintextCode);
        var storedRowId = _fixture.Create<Guid>();
        var storedRow = new UserMfaRecoveryCode(storedRowId)
        {
            UserId = callerId,
            CodeHash = storedHash,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.UserManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        context.MfaTotpValidatorMock
            .Setup(x => x.ValidateCode(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns(false);
        context.RecoveryCodeRepositoryMock
            .Setup(x => x.ListUnusedByUserAsync(callerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { storedRow });
        context.RecoveryCodeRepositoryMock
            .Setup(x => x.MarkUsedAsync(storedRowId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DisableMfaCommand(plaintextCode), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        user.IsMfaEnabled.Should().BeFalse();
        user.MfaSecretKey.Should().BeNull();

        context.RecoveryCodeRepositoryMock.Verify(
            x => x.MarkUsedAsync(storedRowId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyEventLogged(context, callerId, SecurityEventType.MfaRecoveryCodeUsed);
    }

    /// <summary>
    /// Both TOTP and recovery code paths fail: handler returns MFA_INVALID_CODE and
    /// does not mutate the user.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeIsInvalid_ShouldReturnMfaInvalidCode()
    {
        // Arrange
        var context = new DisableMfaCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isEnabled: true, hasSecret: true);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.MfaTotpValidatorMock
            .Setup(x => x.ValidateCode(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns(false);
        context.RecoveryCodeRepositoryMock
            .Setup(x => x.ListUnusedByUserAsync(callerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserMfaRecoveryCode>());

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DisableMfaCommand("000000"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MFA_INVALID_CODE");
        user.IsMfaEnabled.Should().BeTrue();
        user.MfaSecretKey.Should().NotBeNull();
        context.UserManagerMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    /// <summary>
    /// MFA is not enabled on the account — the handler short-circuits with MFA_NOT_CONFIGURED.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMfaNotEnabled_ShouldReturnMfaNotConfigured()
    {
        // Arrange
        var context = new DisableMfaCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isEnabled: false, hasSecret: true);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DisableMfaCommand("123456"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MFA_NOT_CONFIGURED");
        context.MfaTotpValidatorMock.Verify(
            x => x.ValidateCode(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
        context.UserManagerMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    /// <summary>
    /// Disabling MFA also soft-deletes every recovery-code row (post-TOTP success).
    /// </summary>
    [Fact]
    public async Task Handle_WhenTotpPathSucceeds_ShouldDeleteAllRecoveryCodes()
    {
        // Arrange
        var context = new DisableMfaCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isEnabled: true, hasSecret: true);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.UserManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        context.MfaTotpValidatorMock
            .Setup(x => x.ValidateCode(user.MfaSecretKey!, "111111"))
            .Returns(true);
        context.RecoveryCodeRepositoryMock
            .Setup(x => x.DeleteAllByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DisableMfaCommand("111111"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        context.RecoveryCodeRepositoryMock.Verify(
            x => x.DeleteAllByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ApplicationUser CreateUser(Guid userId, bool isEnabled, bool hasSecret)
    {
        return new ApplicationUser
        {
            Id = userId,
            UserName = $"join.user{userId:N}",
            Email = "user@join.com",
            IsActive = true,
            GcRecord = 0,
            IsMfaEnabled = isEnabled,
            MfaSecretKey = hasSecret ? "JBSWY3DPEHPK3PXP" : null,
            MfaEnabledAtUtc = isEnabled ? DateTime.UtcNow.AddMinutes(-1) : null
        };
    }

    private static void VerifyEventLogged(
        DisableMfaCommandHandlerTestContext context,
        Guid expectedUserId,
        SecurityEventType expectedType)
    {
        context.SecurityEventLoggerMock.Verify(
            x => x.LogAsync(
                expectedType,
                SecurityEventResult.Success,
                expectedUserId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Self-contained mocks for DisableMfa dependencies.
    /// </summary>
    private sealed class DisableMfaCommandHandlerTestContext
    {
        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } = CreateUserManagerMock();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IMfaTotpValidator> MfaTotpValidatorMock { get; } = new();
        public Mock<IUserMfaRecoveryCodeRepository> RecoveryCodeRepositoryMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();

        public void SetUser(Guid userId)
        {
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.IpAddress).Returns("127.0.0.1");
            CurrentUserServiceMock.SetupGet(x => x.UserAgent).Returns("unit-test");
        }

        public DisableMfaCommandHandler CreateHandler() =>
            new(
                UserManagerMock.Object,
                CurrentUserServiceMock.Object,
                MfaTotpValidatorMock.Object,
                RecoveryCodeRepositoryMock.Object,
                SecurityEventLoggerMock.Object);

        private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(
                store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        }
    }
}
