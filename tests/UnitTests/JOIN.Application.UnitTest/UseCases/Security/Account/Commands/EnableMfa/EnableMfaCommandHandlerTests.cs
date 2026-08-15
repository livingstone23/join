using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Account.Commands.EnableMfa;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Commands.EnableMfa;

/// <summary>
/// Unit tests for the <c>POST /api/v1/account/mfa/enable</c> handler (SPEC 26 / F6).
/// </summary>
public sealed class EnableMfaCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Happy path: a valid TOTP code flips <c>IsMfaEnabled</c> and stamps <c>MfaEnabledAtUtc</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTotpCodeIsValid_ShouldEnableMfa()
    {
        // Arrange
        var context = new EnableMfaCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, hasSecret: true);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.UserManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        context.MfaTotpValidatorMock
            .Setup(x => x.ValidateCode(user.MfaSecretKey!, "123456"))
            .Returns(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new EnableMfaCommand("123456"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        user.IsMfaEnabled.Should().BeTrue();
        user.MfaEnabledAtUtc.Should().NotBeNull();
        user.MfaEnabledAtUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        VerifyEventLogged(context, callerId, SecurityEventType.MfaEnabled);
    }

    /// <summary>
    /// TOTP validation rejects the supplied code: handler returns <c>MFA_INVALID_CODE</c> and
    /// does not touch <c>IsMfaEnabled</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTotpCodeIsInvalid_ShouldReturnMfaInvalidCode()
    {
        // Arrange
        var context = new EnableMfaCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, hasSecret: true);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.MfaTotpValidatorMock
            .Setup(x => x.ValidateCode(user.MfaSecretKey!, "000000"))
            .Returns(false);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new EnableMfaCommand("000000"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MFA_INVALID_CODE");
        user.IsMfaEnabled.Should().BeFalse();
        user.MfaEnabledAtUtc.Should().BeNull();

        context.UserManagerMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
        context.SecurityEventLoggerMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// No-secret path: user has never run setup. Handler returns MFA_NOT_CONFIGURED.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasNoSecret_ShouldReturnMfaNotConfigured()
    {
        // Arrange
        var context = new EnableMfaCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, hasSecret: false);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new EnableMfaCommand("123456"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MFA_NOT_CONFIGURED");
        context.MfaTotpValidatorMock.Verify(x => x.ValidateCode(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        context.UserManagerMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    /// <summary>
    /// Edge case: setup has run (secret present) but recovery-code set is empty and the
    /// recovery codes are not consumed by this endpoint. The TOTP path still works.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSecretExistsAndRecoveryCodesIntact_ShouldStillEnableViaTotp()
    {
        // Arrange
        var context = new EnableMfaCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, hasSecret: true);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.UserManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        context.MfaTotpValidatorMock
            .Setup(x => x.ValidateCode(user.MfaSecretKey!, "424242"))
            .Returns(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new EnableMfaCommand("424242"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        user.IsMfaEnabled.Should().BeTrue();
        // Recovery codes are NOT touched by EnableMfa — that contract is verified by DisableMfa.
    }

    private static ApplicationUser CreateUser(Guid userId, bool hasSecret)
    {
        return new ApplicationUser
        {
            Id = userId,
            UserName = $"join.user{userId:N}",
            Email = "user@join.com",
            IsActive = true,
            GcRecord = 0,
            MfaSecretKey = hasSecret ? "JBSWY3DPEHPK3PXP" : null
        };
    }

    private static void VerifyEventLogged(
        EnableMfaCommandHandlerTestContext context,
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
    /// Self-contained mocks for EnableMfa dependencies.
    /// </summary>
    private sealed class EnableMfaCommandHandlerTestContext
    {
        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } = CreateUserManagerMock();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IMfaTotpValidator> MfaTotpValidatorMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();

        public void SetUser(Guid userId)
        {
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.IpAddress).Returns("127.0.0.1");
            CurrentUserServiceMock.SetupGet(x => x.UserAgent).Returns("unit-test");
        }

        public EnableMfaCommandHandler CreateHandler() =>
            new(
                UserManagerMock.Object,
                CurrentUserServiceMock.Object,
                MfaTotpValidatorMock.Object,
                SecurityEventLoggerMock.Object);

        private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(
                store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        }
    }
}
