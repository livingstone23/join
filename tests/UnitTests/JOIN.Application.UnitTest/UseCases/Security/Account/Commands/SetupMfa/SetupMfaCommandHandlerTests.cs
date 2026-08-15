using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Account.Commands.SetupMfa;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Commands.SetupMfa;

/// <summary>
/// Unit tests for the <c>POST /api/v1/account/mfa/setup</c> handler (SPEC 26 / F6).
/// </summary>
public sealed class SetupMfaCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Happy path: a fresh secret + 10 recovery codes are issued and MFA stays disabled
    /// until the explicit enable call.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIsValid_ShouldReturnSecretQrAndCodesAndLeaveIsMfaEnabledFalse()
    {
        // Arrange
        var context = new SetupMfaCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);

        context.SetUser(callerId);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(callerId.ToString()))
            .ReturnsAsync(user);

        context.UserManagerMock
            .Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        context.MfaTotpValidatorMock
            .Setup(x => x.GenerateSecret())
            .Returns("JBSWY3DPEHPK3PXP");
        context.MfaTotpValidatorMock
            .Setup(x => x.BuildQrCodeUri("JBSWY3DPEHPK3PXP", user.Email!, "JOIN"))
            .Returns("otpauth://totp/JOIN:user@join.com?secret=JBSWY3DPEHPK3PXP&issuer=JOIN");

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new SetupMfaCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data!.Secret.Should().Be("JBSWY3DPEHPK3PXP");
        response.Data.QrCodeUri.Should().StartWith("otpauth://totp/");
        response.Data.RecoveryCodes.Should().HaveCount(10);
        user.IsMfaEnabled.Should().BeFalse();
        user.MfaSecretKey.Should().Be(response.Data.Secret);

        context.RecoveryCodeRepositoryMock.Verify(
            x => x.DeleteAllByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        context.RecoveryCodeRepositoryMock.Verify(
            x => x.InsertManyAsync(It.Is<IEnumerable<UserMfaRecoveryCode>>(codes => codes.Count() == 10),
                It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyEventLogged(context, callerId, SecurityEventType.MfaSetupInitiated);
    }

    /// <summary>
    /// Re-setup branch: a user who already enrolled but never confirmed rotates their
    /// secret. Their previous recovery-code set must be wiped before the new codes land.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserAlreadyHasSecret_ShouldWipeOldCodesBeforeInsertingNew()
    {
        // Arrange
        var context = new SetupMfaCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);
        user.MfaSecretKey = "OLD-SECRET";
        user.IsMfaEnabled = false;

        context.SetUser(callerId);

        context.UserManagerMock
            .Setup(x => x.FindByIdAsync(callerId.ToString()))
            .ReturnsAsync(user);

        context.UserManagerMock
            .Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        context.MfaTotpValidatorMock.Setup(x => x.GenerateSecret()).Returns("JBSWY3DPEHPK3PXP");
        context.MfaTotpValidatorMock
            .Setup(x => x.BuildQrCodeUri("JBSWY3DPEHPK3PXP", user.Email!, "JOIN"))
            .Returns("otpauth://totp/JOIN:user@join.com?secret=JBSWY3DPEHPK3PXP&issuer=JOIN");

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new SetupMfaCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        user.MfaSecretKey.Should().NotBe("OLD-SECRET");

        // Old codes wiped + new ones inserted, in that order.
        context.RecoveryCodeRepositoryMock.Verify(
            x => x.DeleteAllByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        context.RecoveryCodeRepositoryMock.Verify(
            x => x.InsertManyAsync(It.IsAny<IEnumerable<UserMfaRecoveryCode>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Recovery-code persistence sanity: codes are stored hashed, never in plaintext,
    /// and the response contains the plaintext exactly once (the client shows it now).
    /// </summary>
    [Fact]
    public async Task Handle_WhenRecoveryCodesGenerated_ShouldPersistHashesNotPlaintext()
    {
        // Arrange
        var context = new SetupMfaCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId);

        context.SetUser(callerId);
        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.UserManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        context.MfaTotpValidatorMock.Setup(x => x.GenerateSecret()).Returns("JBSWY3DPEHPK3PXP");
        context.MfaTotpValidatorMock
            .Setup(x => x.BuildQrCodeUri("JBSWY3DPEHPK3PXP", user.Email!, "JOIN"))
            .Returns("otpauth://totp/JOIN:user@join.com?secret=JBSWY3DPEHPK3PXP&issuer=JOIN");

        IEnumerable<UserMfaRecoveryCode>? capturedCodes = null;
        context.RecoveryCodeRepositoryMock
            .Setup(x => x.InsertManyAsync(It.IsAny<IEnumerable<UserMfaRecoveryCode>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<UserMfaRecoveryCode>, CancellationToken>((codes, _) => capturedCodes = codes)
            .ReturnsAsync(10);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new SetupMfaCommand(), CancellationToken.None);

        // Assert
        capturedCodes.Should().NotBeNull();
        var persistedHashes = capturedCodes!.Select(c => c.CodeHash).ToList();
        persistedHashes.Should().HaveCount(10);
        persistedHashes.Should().OnlyContain(hash => hash.Contains(':'));

        // Each plaintext code in the response must round-trip through one of the persisted hashes.
        foreach (var plaintext in response.Data!.RecoveryCodes)
        {
            persistedHashes.Should().Contain(hash => RecoveryCodeHasher.Verify(plaintext, hash));
            persistedHashes.Should().NotContain(plaintext); // no plaintext leakage into storage.
        }
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
        SetupMfaCommandHandlerTestContext context,
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
    /// Self-contained mocks for SetupMfa dependencies.
    /// </summary>
    private sealed class SetupMfaCommandHandlerTestContext
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

        public SetupMfaCommandHandler CreateHandler() =>
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
