using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common.Options;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Users.Commands.ForceUserPasswordReset;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Users.Commands.ForceUserPasswordReset;

/// <summary>
/// Unit tests for <see cref="ForceUserPasswordResetCommandHandler"/>.
/// </summary>
public sealed class ForceUserPasswordResetCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenActiveUserAndEmailDelivered_ShouldRevokeRefreshTokens()
    {
        var ctx = new Context();
        var targetUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var user = new ApplicationUser { Id = targetUserId, Email = "u@x.com", FirstName = "U", IsActive = true };
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.UserAdminRepositoryMock.Setup(x => x.GetAdminSnapshotAsync(targetUserId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAdminSnapshot(targetUserId, user.Email, user.FirstName, IsActive: true, GcRecord: 0, HasMembership: true));
        ctx.UserManagerMock.Setup(x => x.FindByIdAsync(targetUserId.ToString())).ReturnsAsync(user);
        ctx.UserManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("token");
        ctx.EmailServiceMock.Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var response = await ctx.Handler.Handle(
            new ForceUserPasswordResetCommand(targetUserId, "audit"),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        ctx.UserAdminRepositoryMock.Verify(x => x.RevokeActiveRefreshTokensAsync(targetUserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmailSendFails_ShouldNotRevokeRefreshTokens()
    {
        var ctx = new Context();
        var targetUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var user = new ApplicationUser { Id = targetUserId, Email = "u@x.com", FirstName = "U", IsActive = true };
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.UserAdminRepositoryMock.Setup(x => x.GetAdminSnapshotAsync(targetUserId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAdminSnapshot(targetUserId, user.Email, user.FirstName, IsActive: true, GcRecord: 0, HasMembership: true));
        ctx.UserManagerMock.Setup(x => x.FindByIdAsync(targetUserId.ToString())).ReturnsAsync(user);
        ctx.UserManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("token");
        ctx.EmailServiceMock.Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

        var response = await ctx.Handler.Handle(
            new ForceUserPasswordResetCommand(targetUserId, null),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_DELIVERY_FAILED");
        ctx.UserAdminRepositoryMock.Verify(x => x.RevokeActiveRefreshTokensAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSnapshotIsNull_ShouldReturnUserNotFound()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.UserAdminRepositoryMock.Setup(x => x.GetAdminSnapshotAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAdminSnapshot?)null);

        var response = await ctx.Handler.Handle(
            new ForceUserPasswordResetCommand(Guid.NewGuid(), null),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenUserHasNoMembership_ShouldReturnUserNotFound()
    {
        var ctx = new Context();
        var targetUserId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.UserAdminRepositoryMock.Setup(x => x.GetAdminSnapshotAsync(targetUserId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAdminSnapshot(targetUserId, "u@x.com", "U", IsActive: true, GcRecord: 0, HasMembership: false));

        var response = await ctx.Handler.Handle(
            new ForceUserPasswordResetCommand(targetUserId, null),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenUserIsInactive_ShouldReturnUserInactive()
    {
        var ctx = new Context();
        var targetUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.UserAdminRepositoryMock.Setup(x => x.GetAdminSnapshotAsync(targetUserId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAdminSnapshot(targetUserId, "u@x.com", "U", IsActive: false, GcRecord: 0, HasMembership: true));

        var response = await ctx.Handler.Handle(
            new ForceUserPasswordResetCommand(targetUserId, null),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_INACTIVE");
    }

    private sealed class Context
    {
        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } =
            new Mock<UserManager<ApplicationUser>>(
                Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

        public Mock<IUserAdminRepository> UserAdminRepositoryMock { get; } = new();
        public Mock<IEmailService> EmailServiceMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public IOptions<AppUrlsOptions> AppUrls { get; } =
            Options.Create(new AppUrlsOptions { FrontendBaseUrl = "https://app.join.com" });

        public ForceUserPasswordResetCommandHandler Handler => new(
            UserManagerMock.Object,
            UserAdminRepositoryMock.Object,
            EmailServiceMock.Object,
            CurrentUserServiceMock.Object,
            AppUrls);
    }
}
