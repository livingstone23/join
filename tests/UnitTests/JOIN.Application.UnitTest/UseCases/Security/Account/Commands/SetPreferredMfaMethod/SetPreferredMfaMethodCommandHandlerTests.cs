using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Account.Commands.SetPreferredMfaMethod;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Commands.SetPreferredMfaMethod;

/// <summary>
/// Unit tests for the <c>PUT /api/v1/account/mfa/preferred-method</c> handler (SPEC 32 / F8).
/// </summary>
public sealed class SetPreferredMfaMethodCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Happy path: "totp" is accepted when the user has TOTP active, and persists.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTotpIsActiveForUser_ShouldSetPreferredMethod()
    {
        // Arrange
        var context = new SetPreferredMfaMethodCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isMfaEnabled: true, isEmailOtpEnabled: true);

        context.SetUser(callerId);
        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.UserManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new SetPreferredMfaMethodCommand("totp"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        user.PreferredMfaMethod.Should().Be("totp");

        context.SecurityEventLoggerMock.Verify(
            x => x.LogAsync(
                SecurityEventType.MfaPreferredMethodChanged,
                SecurityEventResult.Success,
                callerId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Happy path: method comparison is case-insensitive and trims whitespace.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMethodHasWhitespaceAndMixedCase_ShouldNormalizeAndSet()
    {
        // Arrange
        var context = new SetPreferredMfaMethodCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isMfaEnabled: false, isEmailOtpEnabled: true);

        context.SetUser(callerId);
        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.UserManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new SetPreferredMfaMethodCommand("  EMAIL  "), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        user.PreferredMfaMethod.Should().Be("email");
    }

    /// <summary>
    /// Rejects a method that is not currently active for the user, even though it is a
    /// recognized method name.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMethodIsNotActiveForUser_ShouldReturnPreferredMethodNotEnabled()
    {
        // Arrange
        var context = new SetPreferredMfaMethodCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isMfaEnabled: true, isEmailOtpEnabled: false);

        context.SetUser(callerId);
        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new SetPreferredMfaMethodCommand("email"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PREFERRED_METHOD_NOT_ENABLED");
        user.PreferredMfaMethod.Should().BeNull();

        context.UserManagerMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    /// <summary>
    /// Rejects a value that isn't a recognized method at all.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMethodIsUnknown_ShouldReturnPreferredMethodNotEnabled()
    {
        // Arrange
        var context = new SetPreferredMfaMethodCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, isMfaEnabled: true, isEmailOtpEnabled: true);

        context.SetUser(callerId);
        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new SetPreferredMfaMethodCommand("sms"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PREFERRED_METHOD_NOT_ENABLED");
    }

    private static ApplicationUser CreateUser(Guid userId, bool isMfaEnabled, bool isEmailOtpEnabled)
    {
        return new ApplicationUser
        {
            Id = userId,
            UserName = $"join.user{userId:N}",
            Email = "user@join.com",
            IsActive = true,
            IsMfaEnabled = isMfaEnabled,
            IsEmailOtpEnabled = isEmailOtpEnabled,
            GcRecord = 0
        };
    }

    private sealed class SetPreferredMfaMethodCommandHandlerTestContext
    {
        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } = CreateUserManagerMock();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();

        public void SetUser(Guid userId)
        {
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.IpAddress).Returns("127.0.0.1");
            CurrentUserServiceMock.SetupGet(x => x.UserAgent).Returns("unit-test");
        }

        public SetPreferredMfaMethodCommandHandler CreateHandler() =>
            new(
                UserManagerMock.Object,
                CurrentUserServiceMock.Object,
                SecurityEventLoggerMock.Object);

        private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(
                store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        }
    }
}
