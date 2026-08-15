using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Account.Commands.ConfirmEmailChange;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Commands.ConfirmEmailChange;

/// <summary>
/// Unit tests for the <c>POST /api/v1/account/confirm-email-change</c> handler
/// (SPEC 26 / F7).
/// </summary>
public sealed class ConfirmEmailChangeCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Happy path: a valid Identity token flips the user's email + EmailConfirmed and
    /// logs EmailChangeConfirmed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenChangeEmailSucceeds_ShouldUpdateUserAndLogConfirmed()
    {
        // Arrange
        var context = new ConfirmEmailChangeCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, emailConfirmed: true);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.UserManagerMock
            .Setup(x => x.ChangeEmailAsync(user, "new@join.com", "valid-token"))
            .Callback<ApplicationUser, string, string>((u, newEmail, _) =>
            {
                // Mock Identity's success path so the assertions can observe the post-change user.
                typeof(ApplicationUser).GetProperty(nameof(ApplicationUser.Email))!
                    .SetValue(u, newEmail);
            })
            .ReturnsAsync(IdentityResult.Success);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new ConfirmEmailChangeCommand("new@join.com", "valid-token"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        user.Email.Should().Be("new@join.com");

        VerifyEventLogged(context, callerId, SecurityEventType.EmailChangeConfirmed);
    }

    /// <summary>
    /// Invalid token: Identity returns Errors. Handler returns EMAIL_CHANGE_FAILED and
    /// logs EmailChangeFailed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTokenIsInvalid_ShouldReturnEmailChangeFailed()
    {
        // Arrange
        var context = new ConfirmEmailChangeCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, emailConfirmed: true);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.UserManagerMock
            .Setup(x => x.ChangeEmailAsync(user, "new@join.com", "expired-token"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError
            {
                Code = "InvalidToken",
                Description = "Token is invalid."
            }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new ConfirmEmailChangeCommand("new@join.com", "expired-token"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_CHANGE_FAILED");

        VerifyEventLogged(context, callerId, SecurityEventType.EmailChangeFailed);
    }

    /// <summary>
    /// Mismatched newEmail: the request Body carries one address but the token was issued
    /// against a different one. Identity rejects the call because the token does not
    /// match. Handler returns EMAIL_CHANGE_FAILED.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNewEmailDoesNotMatchTokenIntent_ShouldReturnEmailChangeFailed()
    {
        // Arrange
        var context = new ConfirmEmailChangeCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, emailConfirmed: true);

        context.SetUser(callerId);

        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);
        context.UserManagerMock
            .Setup(x => x.ChangeEmailAsync(user, "attacker@evil.com", "valid-token"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError
            {
                Code = "InvalidToken",
                Description = "Token does not match the supplied email."
            }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new ConfirmEmailChangeCommand("attacker@evil.com", "valid-token"), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_CHANGE_FAILED");
        user.Email.Should().NotBe("attacker@evil.com");

        VerifyEventLogged(context, callerId, SecurityEventType.EmailChangeFailed);
    }

    private static ApplicationUser CreateUser(Guid userId, bool emailConfirmed)
    {
        return new ApplicationUser
        {
            Id = userId,
            UserName = $"join.user{userId:N}",
            Email = "user@join.com",
            EmailConfirmed = emailConfirmed,
            IsActive = true,
            GcRecord = 0
        };
    }

    private static void VerifyEventLogged(
        ConfirmEmailChangeCommandHandlerTestContext context,
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
    /// Self-contained mocks for ConfirmEmailChange dependencies.
    /// </summary>
    private sealed class ConfirmEmailChangeCommandHandlerTestContext
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

        public ConfirmEmailChangeCommandHandler CreateHandler() =>
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
