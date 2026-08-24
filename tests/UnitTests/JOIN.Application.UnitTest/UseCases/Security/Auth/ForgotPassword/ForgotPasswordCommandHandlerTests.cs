using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common.Options;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Auth.ForgotPassword;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Auth.ForgotPassword;

/// <summary>
/// Unit tests for <see cref="ForgotPasswordCommandHandler"/>. Verifies enumeration-safe
/// behavior (no email → no <c>SendEmailAsync</c> call), happy path (email sent), and the
/// failure path (provider error → <c>EMAIL_DELIVERY_FAILED</c>).
/// </summary>
public sealed class ForgotPasswordCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ShouldReturnSuccessWithoutSendingEmail()
    {
        var ctx = new Context();
        ctx.UserManagerMock
            .Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var response = await ctx.Handler.Handle(
            new ForgotPasswordCommand { Email = "unknown@example.com" },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        ctx.EmailServiceMock.Verify(
            x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserExists_ShouldSendResetEmail()
    {
        var ctx = new Context();
        var user = new ApplicationUser { Email = "user@example.com", FirstName = "Test", IsActive = true };
        ctx.UserManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        ctx.UserManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("token");
        ctx.EmailServiceMock.Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var response = await ctx.Handler.Handle(
            new ForgotPasswordCommand { Email = "user@example.com" },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        ctx.EmailServiceMock.Verify(
            x => x.SendEmailAsync(user.Email, It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmailProviderFails_ShouldReturnEmailDeliveryFailed()
    {
        var ctx = new Context();
        var user = new ApplicationUser { Email = "user@example.com", FirstName = "Test", IsActive = true };
        ctx.UserManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        ctx.UserManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("token");
        ctx.EmailServiceMock.Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

        var response = await ctx.Handler.Handle(
            new ForgotPasswordCommand { Email = "user@example.com" },
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_DELIVERY_FAILED");
    }

    private sealed class Context
    {
        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } =
            new Mock<UserManager<ApplicationUser>>(
                Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

        public Mock<IEmailService> EmailServiceMock { get; } = new();

        public IOptions<AppUrlsOptions> AppUrls { get; } =
            Options.Create(new AppUrlsOptions { FrontendBaseUrl = "https://app.join.com" });

        public ForgotPasswordCommandHandler Handler =>
            new(UserManagerMock.Object, EmailServiceMock.Object, AppUrls);
    }
}
