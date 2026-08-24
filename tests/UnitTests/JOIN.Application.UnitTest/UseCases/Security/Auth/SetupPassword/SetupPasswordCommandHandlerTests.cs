using AutoFixture;
using FluentAssertions;
using JOIN.Application.UseCases.Security.Auth.SetupPassword;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Auth.SetupPassword;

/// <summary>
/// Unit tests for <see cref="SetupPasswordCommandHandler"/>.
/// </summary>
public sealed class SetupPasswordCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenTokenIsValidAndPasswordNotSet_ShouldSetPasswordAndConfirmEmail()
    {
        var ctx = new Context();
        var user = new ApplicationUser { Email = "user@example.com", IsActive = true, EmailConfirmed = false };
        ctx.UserManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        ctx.UserManagerMock.Setup(x => x.HasPasswordAsync(user)).ReturnsAsync(false);
        ctx.UserManagerMock.Setup(x => x.ResetPasswordAsync(user, "token", "Passw0rd!"))
            .ReturnsAsync(IdentityResult.Success);
        ctx.UserManagerMock.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        var response = await ctx.Handler.Handle(
            new SetupPasswordCommand { Email = user.Email, Token = "token", NewPassword = "Passw0rd!", ConfirmPassword = "Passw0rd!" },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        user.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenPasswordAlreadySet_ShouldReturnPasswordAlreadySet()
    {
        var ctx = new Context();
        var user = new ApplicationUser { Email = "user@example.com", IsActive = true };
        ctx.UserManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        ctx.UserManagerMock.Setup(x => x.HasPasswordAsync(user)).ReturnsAsync(true);

        var response = await ctx.Handler.Handle(
            new SetupPasswordCommand { Email = user.Email, Token = "token", NewPassword = "Passw0rd!", ConfirmPassword = "Passw0rd!" },
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PASSWORD_ALREADY_SET");
    }

    [Fact]
    public async Task Handle_WhenTokenIsInvalid_ShouldReturnInvalidToken()
    {
        var ctx = new Context();
        var user = new ApplicationUser { Email = "user@example.com", IsActive = true };
        ctx.UserManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        ctx.UserManagerMock.Setup(x => x.HasPasswordAsync(user)).ReturnsAsync(false);
        ctx.UserManagerMock.Setup(x => x.ResetPasswordAsync(user, "bad", "Passw0rd!"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Invalid token." }));

        var response = await ctx.Handler.Handle(
            new SetupPasswordCommand { Email = user.Email, Token = "bad", NewPassword = "Passw0rd!", ConfirmPassword = "Passw0rd!" },
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_TOKEN");
    }

    [Fact]
    public async Task Handle_WhenEmailDoesNotExist_ShouldReturnInvalidToken()
    {
        var ctx = new Context();
        ctx.UserManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        var response = await ctx.Handler.Handle(
            new SetupPasswordCommand { Email = "missing@example.com", Token = "token", NewPassword = "Passw0rd!", ConfirmPassword = "Passw0rd!" },
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_TOKEN");
    }

    private sealed class Context
    {
        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } =
            new Mock<UserManager<ApplicationUser>>(
                Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

        public SetupPasswordCommandHandler Handler => new(UserManagerMock.Object);
    }
}
