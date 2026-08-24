using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common.Options;
using JOIN.Application.DTO.Security.User;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Users.Commands.InviteUser;
using JOIN.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Users.Commands.InviteUser;

/// <summary>
/// Unit tests for <see cref="InviteUserCommandHandler"/> — verifies the four-branch
/// decision tree (Created, MembershipAdded, InvitationResent, USER_ALREADY_EXISTS) plus
/// the failure paths (role not found in tenant, email provider failure).
/// </summary>
public sealed class InviteUserCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenEmailIsNew_ShouldCreateUserAndReturnCreatedOutcome()
    {
        var ctx = new Context();
        var roleId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.UserAdminRepositoryMock.Setup(x => x.FilterExistingRoleIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { roleId });
        ctx.UserManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        ctx.UserManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
        ctx.UserAdminRepositoryMock.Setup(x => x.HasAnyCompanyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        ctx.UserAdminRepositoryMock.Setup(x => x.GetCompanyNameAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync("Acme");
        ctx.UserManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>())).ReturnsAsync("token");
        ctx.EmailServiceMock.Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var response = await ctx.Handler.Handle(
            new InviteUserCommand("new@example.com", "John", "Doe", new[] { roleId }),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Outcome.Should().Be(InviteOutcome.Created);
        ctx.UnitOfWorkMock.Verify(x => x.GetRepository<UserCompany>(), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenExistingUserWithoutMembership_ShouldReturnMembershipAdded()
    {
        var ctx = new Context();
        var existingUser = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com", IsActive = true };
        var roleId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.UserAdminRepositoryMock.Setup(x => x.FilterExistingRoleIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { roleId });
        ctx.UserManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(existingUser);
        ctx.UserAdminRepositoryMock.Setup(x => x.HasCompanyMembershipAsync(existingUser.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        ctx.UserAdminRepositoryMock.Setup(x => x.HasAnyCompanyAsync(existingUser.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ctx.UserAdminRepositoryMock.Setup(x => x.GetCompanyNameAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync("Acme");
        ctx.UserManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(existingUser)).ReturnsAsync("token");
        ctx.EmailServiceMock.Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var response = await ctx.Handler.Handle(
            new InviteUserCommand("user@example.com", "John", "Doe", new[] { roleId }),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Outcome.Should().Be(InviteOutcome.MembershipAdded);
    }

    [Fact]
    public async Task Handle_WhenExistingUserWithMembershipAndNoPassword_ShouldReturnInvitationResent()
    {
        var ctx = new Context();
        var existingUser = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com", IsActive = true };
        var roleId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.UserAdminRepositoryMock.Setup(x => x.FilterExistingRoleIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { roleId });
        ctx.UserManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(existingUser);
        ctx.UserAdminRepositoryMock.Setup(x => x.HasCompanyMembershipAsync(existingUser.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ctx.UserManagerMock.Setup(x => x.HasPasswordAsync(existingUser)).ReturnsAsync(false);
        ctx.UserManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(existingUser)).ReturnsAsync("token");
        ctx.UserAdminRepositoryMock.Setup(x => x.GetCompanyNameAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync("Acme");
        ctx.EmailServiceMock.Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var response = await ctx.Handler.Handle(
            new InviteUserCommand("user@example.com", "John", "Doe", new[] { roleId }),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Outcome.Should().Be(InviteOutcome.InvitationResent);
    }

    [Fact]
    public async Task Handle_WhenExistingUserWithPassword_ShouldReturnUserAlreadyExists()
    {
        var ctx = new Context();
        var existingUser = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com", IsActive = true };
        var roleId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.UserAdminRepositoryMock.Setup(x => x.FilterExistingRoleIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { roleId });
        ctx.UserManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(existingUser);
        ctx.UserAdminRepositoryMock.Setup(x => x.HasCompanyMembershipAsync(existingUser.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ctx.UserManagerMock.Setup(x => x.HasPasswordAsync(existingUser)).ReturnsAsync(true);

        var response = await ctx.Handler.Handle(
            new InviteUserCommand("user@example.com", "John", "Doe", new[] { roleId }),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_ALREADY_EXISTS");
    }

    [Fact]
    public async Task Handle_WhenTenantIsEmpty_ShouldReturnTenantRequired()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var response = await ctx.Handler.Handle(
            new InviteUserCommand("user@example.com", "John", "Doe", new[] { Guid.NewGuid() }),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TENANT_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenRoleIsFromAnotherTenant_ShouldReturnRoleNotFound()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.UserAdminRepositoryMock.Setup(x => x.FilterExistingRoleIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        var response = await ctx.Handler.Handle(
            new InviteUserCommand("user@example.com", "John", "Doe", new[] { Guid.NewGuid(), Guid.NewGuid() }),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenEmailProviderFails_ShouldReturnEmailDeliveryFailed()
    {
        var ctx = new Context();
        var roleId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.UserAdminRepositoryMock.Setup(x => x.FilterExistingRoleIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { roleId });
        ctx.UserManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        ctx.UserManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        ctx.UserAdminRepositoryMock.Setup(x => x.HasAnyCompanyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        ctx.UserAdminRepositoryMock.Setup(x => x.GetCompanyNameAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync("Acme");
        ctx.UserManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>())).ReturnsAsync("token");
        ctx.EmailServiceMock.Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

        var response = await ctx.Handler.Handle(
            new InviteUserCommand("new@example.com", "John", "Doe", new[] { roleId }),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("EMAIL_DELIVERY_FAILED");
    }

    private sealed class Context
    {
        public Mock<UserManager<ApplicationUser>> UserManagerMock { get; } =
            new Mock<UserManager<ApplicationUser>>(
                Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IUserAdminRepository> UserAdminRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IEmailService> EmailServiceMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();
        public IOptions<AppUrlsOptions> AppUrls { get; } =
            Options.Create(new AppUrlsOptions { FrontendBaseUrl = "https://app.join.com" });

        public InviteUserCommandHandler Handler => new(
            UserManagerMock.Object,
            UnitOfWorkMock.Object,
            UserAdminRepositoryMock.Object,
            CurrentUserServiceMock.Object,
            EmailServiceMock.Object,
            AppUrls,
            SecurityEventLoggerMock.Object);
    }
}
