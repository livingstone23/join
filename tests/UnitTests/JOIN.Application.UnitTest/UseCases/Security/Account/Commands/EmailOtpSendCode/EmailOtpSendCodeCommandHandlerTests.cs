using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Account.Commands.EmailOtpSendCode;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Commands.EmailOtpSendCode;

/// <summary>
/// Unit tests for the <c>POST /api/v1/account/email-otp/send-code</c> handler (SPEC 32 / F7).
/// </summary>
public sealed class EmailOtpSendCodeCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Happy path: a confirmed-email user triggers invalidation + insert + email dispatch.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEmailIsConfirmed_ShouldInvalidateInsertAndSendEmail()
    {
        // Arrange
        var context = new EmailOtpSendCodeCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, emailConfirmed: true);

        context.SetUser(callerId);
        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);

        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.InvalidateActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        JOIN.Domain.Security.EmailOtpEnableCode? captured = null;
        context.EmailOtpCodeRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<JOIN.Domain.Security.EmailOtpEnableCode>(), It.IsAny<CancellationToken>()))
            .Callback<JOIN.Domain.Security.EmailOtpEnableCode, CancellationToken>((c, _) => captured = c)
            .Returns(Task.CompletedTask);

        context.EmailServiceMock
            .Setup(x => x.SendEmailAsync(user.Email!, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new EmailOtpSendCodeCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(callerId);
        captured.Email.Should().Be(user.Email);
        captured.AttemptCount.Should().Be(0);
        captured.ExpiresAtUtc.Should().BeAfter(captured.Created);

        context.EmailOtpCodeRepositoryMock.Verify(
            x => x.InvalidateActiveByUserAsync(callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);

        context.SecurityEventLoggerMock.Verify(
            x => x.LogAsync(
                SecurityEventType.EmailOtpEnableCodeRequested,
                SecurityEventResult.Success,
                callerId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Rejects the request when the account email is not confirmed — no code is minted or sent.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEmailNotConfirmed_ShouldReturnEmailNotConfirmed()
    {
        // Arrange
        var context = new EmailOtpSendCodeCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var user = CreateUser(callerId, emailConfirmed: false);

        context.SetUser(callerId);
        context.UserManagerMock.Setup(x => x.FindByIdAsync(callerId.ToString())).ReturnsAsync(user);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new EmailOtpSendCodeCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_NOT_CONFIRMED");

        context.EmailOtpCodeRepositoryMock.Verify(
            x => x.InsertAsync(It.IsAny<JOIN.Domain.Security.EmailOtpEnableCode>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.EmailServiceMock.Verify(
            x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
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

    private sealed class EmailOtpSendCodeCommandHandlerTestContext
    {
        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } = CreateUserManagerMock();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IEmailOtpEnableCodeRepository> EmailOtpCodeRepositoryMock { get; } = new();
        public Mock<IEmailService> EmailServiceMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();

        public void SetUser(Guid userId)
        {
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.IpAddress).Returns("127.0.0.1");
            CurrentUserServiceMock.SetupGet(x => x.UserAgent).Returns("unit-test");
        }

        public EmailOtpSendCodeCommandHandler CreateHandler() =>
            new(
                UserManagerMock.Object,
                CurrentUserServiceMock.Object,
                EmailOtpCodeRepositoryMock.Object,
                EmailServiceMock.Object,
                SecurityEventLoggerMock.Object);

        private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(
                store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        }
    }
}
