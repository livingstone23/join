using AutoFixture;
using FluentAssertions;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Auth.Login;

/// <summary>
/// Contains the unit tests for the login credential-validation flow. Company/role resolution and
/// JWT/refresh-token issuance now live in <see cref="AuthenticatedSessionIssuer"/> and are covered by
/// <c>AuthenticatedSessionIssuerTests</c> — this handler only validates credentials and delegates.
/// </summary>
public sealed class LoginCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the unauthorized branch when the email does not match any user.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var request = CreateValidCommand();

        context.UserManagerMock
            .Setup(x => x.FindByEmailAsync(request.Email.Trim()))
            .ReturnsAsync((ApplicationUser?)null);

        var handler = context.CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid email or password.");

        context.AuthenticatedSessionIssuerMock.Verify(
            x => x.IssueAsync(It.IsAny<ApplicationUser>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies the unauthorized branch when the user account is inactive.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIsInactive_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var request = CreateValidCommand();
        var user = CreateUser(isActive: false);

        context.UserManagerMock
            .Setup(x => x.FindByEmailAsync(request.Email.Trim()))
            .ReturnsAsync(user);

        var handler = context.CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("The user account is inactive.");

        context.AuthenticatedSessionIssuerMock.Verify(
            x => x.IssueAsync(It.IsAny<ApplicationUser>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies the unauthorized branch when the user was soft-deleted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIsSoftDeleted_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var request = CreateValidCommand();
        var user = CreateUser();
        user.GcRecord = 20260420;

        context.UserManagerMock
            .Setup(x => x.FindByEmailAsync(request.Email.Trim()))
            .ReturnsAsync(user);

        var handler = context.CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("The user account is inactive.");

        context.AuthenticatedSessionIssuerMock.Verify(
            x => x.IssueAsync(It.IsAny<ApplicationUser>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies the unauthorized branch when the supplied password is invalid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPasswordIsInvalid_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var request = CreateValidCommand();
        var user = CreateUser();

        context.UserManagerMock
            .Setup(x => x.FindByEmailAsync(request.Email.Trim()))
            .ReturnsAsync(user);

        context.UserManagerMock
            .Setup(x => x.CheckPasswordAsync(user, request.Password))
            .ReturnsAsync(false);

        var handler = context.CreateHandler();

        // Act
        Func<Task> act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid email or password.");

        context.AuthenticatedSessionIssuerMock.Verify(
            x => x.IssueAsync(It.IsAny<ApplicationUser>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that once credentials are valid, the handler delegates company/role resolution and
    /// session issuance to <see cref="IAuthenticatedSessionIssuer"/> and returns its response as-is.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCredentialsAreValid_ShouldDelegateToAuthenticatedSessionIssuer()
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var targetCompanyId = _fixture.Create<Guid>();
        var request = CreateValidCommand(targetCompanyId);
        var user = CreateUser();
        var expectedResponse = new LoginResponse
        {
            UserId = user.Id,
            UserName = user.UserName!,
            Email = user.Email!,
            CompanyId = targetCompanyId,
            Roles = ["Admin"],
            Token = "jwt-token",
            RefreshToken = "refresh-token",
            Expiration = DateTime.UtcNow.AddHours(1)
        };

        context.UserManagerMock
            .Setup(x => x.FindByEmailAsync(request.Email.Trim()))
            .ReturnsAsync(user);

        context.UserManagerMock
            .Setup(x => x.CheckPasswordAsync(user, request.Password))
            .ReturnsAsync(true);

        context.AuthenticatedSessionIssuerMock
            .Setup(x => x.IssueAsync(user, targetCompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().BeSameAs(expectedResponse);

        context.AuthenticatedSessionIssuerMock.Verify(
            x => x.IssueAsync(user, targetCompanyId, It.IsAny<CancellationToken>()),
            Times.Once);

        context.MfaLoginChallengeIssuerMock.Verify(
            x => x.IssueChallengeAsync(It.IsAny<ApplicationUser>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        context.SecurityEventLoggerMock.Verify(
            x => x.LogAsync(
                It.IsAny<SecurityEventType>(),
                It.IsAny<SecurityEventResult>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that once credentials are valid, a user with TOTP and/or Email OTP active is
    /// diverted to the MFA challenge issuer instead of the direct session issuer — SPEC 32 F5.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Handle_WhenUserHasActiveMfaMethod_ShouldDelegateToMfaLoginChallengeIssuer(bool isMfaEnabled, bool isEmailOtpEnabled)
    {
        // Arrange
        var context = new LoginCommandHandlerTestContext();
        var targetCompanyId = _fixture.Create<Guid>();
        var request = CreateValidCommand(targetCompanyId);
        var user = CreateUser();
        user.IsMfaEnabled = isMfaEnabled;
        user.IsEmailOtpEnabled = isEmailOtpEnabled;

        var expectedChallengeResponse = new LoginResponse
        {
            UserId = user.Id,
            UserName = user.UserName!,
            Email = user.Email!,
            Roles = [],
            ChallengeToken = "opaque-challenge-token",
            AvailableMethods = ["totp", "email"]
        };

        context.UserManagerMock
            .Setup(x => x.FindByEmailAsync(request.Email.Trim()))
            .ReturnsAsync(user);

        context.UserManagerMock
            .Setup(x => x.CheckPasswordAsync(user, request.Password))
            .ReturnsAsync(true);

        context.MfaLoginChallengeIssuerMock
            .Setup(x => x.IssueChallengeAsync(user, targetCompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedChallengeResponse);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().BeSameAs(expectedChallengeResponse);
        response.Token.Should().BeNull();
        response.RefreshToken.Should().BeNull();
        response.Expiration.Should().BeNull();

        context.MfaLoginChallengeIssuerMock.Verify(
            x => x.IssueChallengeAsync(user, targetCompanyId, It.IsAny<CancellationToken>()),
            Times.Once);

        context.AuthenticatedSessionIssuerMock.Verify(
            x => x.IssueAsync(It.IsAny<ApplicationUser>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Creates a valid login command for the authentication flow.
    /// </summary>
    private static LoginCommand CreateValidCommand(Guid? targetCompanyId = null)
    {
        return new LoginCommand
        {
            Email = "  user@join.com  ",
            Password = "Pass123!",
            TargetCompanyId = targetCompanyId
        };
    }

    /// <summary>
    /// Creates a valid user for the authentication scenarios.
    /// </summary>
    private static ApplicationUser CreateUser(bool isActive = true)
    {
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "join.user",
            Email = "user@join.com",
            IsActive = isActive,
            GcRecord = 0
        };
    }

    /// <summary>
    /// Creates a mockable ASP.NET Core user manager instance.
    /// </summary>
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

    /// <summary>
    /// Holds the reusable mocks and helper factory for the login handler.
    /// </summary>
    private sealed class LoginCommandHandlerTestContext
    {
        public LoginCommandHandlerTestContext()
        {
            // Default caller info for the audit logger (HTTP context without an authenticated user).
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns((string?)null);
            CurrentUserServiceMock.SetupGet(x => x.IpAddress).Returns("127.0.0.1");
            CurrentUserServiceMock.SetupGet(x => x.UserAgent).Returns("unit-test");
        }

        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } = CreateUserManagerMock();
        public Mock<IAuthenticatedSessionIssuer> AuthenticatedSessionIssuerMock { get; } = new();
        public Mock<IMfaLoginChallengeIssuer> MfaLoginChallengeIssuerMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();

        public LoginCommandHandler CreateHandler()
        {
            return new LoginCommandHandler(
                UserManagerMock.Object,
                AuthenticatedSessionIssuerMock.Object,
                MfaLoginChallengeIssuerMock.Object,
                CurrentUserServiceMock.Object,
                SecurityEventLoggerMock.Object);
        }
    }
}
