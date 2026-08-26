using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Users.Commands.ChangeUserStatus;
using Microsoft.Extensions.Logging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Users.Commands.ChangeUserStatus;

/// <summary>
/// Unit tests for <see cref="ChangeUserStatusCommandHandler"/>.
/// </summary>
public sealed class ChangeUserStatusCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenDeactivatingActiveUser_ShouldFlipStatusRevokeTokensAndCloseConnections()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
        ctx.UserAdminRepositoryMock.Setup(x => x.GetAdminSnapshotAsync(targetUserId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAdminSnapshot(targetUserId, "u@x.com", "U", IsActive: true, GcRecord: 0, HasMembership: true, StatusChangeReason: null));
        ctx.UserAdminRepositoryMock.Setup(x => x.SetUserActiveStatusAsync(targetUserId, false, "left", It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await ctx.Handler.Handle(
            new ChangeUserStatusCommand(targetUserId, false, "left"),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        ctx.UserAdminRepositoryMock.Verify(x => x.RevokeActiveRefreshTokensAsync(targetUserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        ctx.UserAdminRepositoryMock.Verify(x => x.CloseActiveConnectionsAsync(targetUserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        ctx.PermissionServiceMock.Verify(x => x.InvalidateUserCacheAsync(companyId, targetUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenActivatingInactiveUser_ShouldFlipAndInvalidateCacheOnly()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
        ctx.UserAdminRepositoryMock.Setup(x => x.GetAdminSnapshotAsync(targetUserId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAdminSnapshot(targetUserId, "u@x.com", "U", IsActive: false, GcRecord: 0, HasMembership: true, StatusChangeReason: null));
        ctx.UserAdminRepositoryMock.Setup(x => x.SetUserActiveStatusAsync(targetUserId, true, "back", It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await ctx.Handler.Handle(
            new ChangeUserStatusCommand(targetUserId, true, "back"),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        ctx.UserAdminRepositoryMock.Verify(x => x.RevokeActiveRefreshTokensAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        ctx.UserAdminRepositoryMock.Verify(x => x.CloseActiveConnectionsAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        ctx.PermissionServiceMock.Verify(x => x.InvalidateUserCacheAsync(companyId, targetUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsTarget_ShouldReturnCannotChangeOwnStatus()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());

        var response = await ctx.Handler.Handle(
            new ChangeUserStatusCommand(userId, false, "x"),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CANNOT_CHANGE_OWN_STATUS");
    }

    [Fact]
    public async Task Handle_WhenSnapshotIsNull_ShouldReturnUserNotFound()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
        ctx.UserAdminRepositoryMock.Setup(x => x.GetAdminSnapshotAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAdminSnapshot?)null);

        var response = await ctx.Handler.Handle(
            new ChangeUserStatusCommand(Guid.NewGuid(), false, "x"),
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
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
        ctx.UserAdminRepositoryMock.Setup(x => x.GetAdminSnapshotAsync(targetUserId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAdminSnapshot(targetUserId, "u@x.com", "U", IsActive: true, GcRecord: 0, HasMembership: false, StatusChangeReason: null));

        var response = await ctx.Handler.Handle(
            new ChangeUserStatusCommand(targetUserId, false, "x"),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenStatusUnchanged_ShouldReturnStatusUnchanged()
    {
        var ctx = new Context();
        var targetUserId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
        ctx.UserAdminRepositoryMock.Setup(x => x.GetAdminSnapshotAsync(targetUserId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAdminSnapshot(targetUserId, "u@x.com", "U", IsActive: true, GcRecord: 0, HasMembership: true, StatusChangeReason: null));

        var response = await ctx.Handler.Handle(
            new ChangeUserStatusCommand(targetUserId, true, "x"),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("STATUS_UNCHANGED");
    }

    [Fact]
    public async Task Handle_WhenPermissionServiceFails_ShouldStillReturnSuccess()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
        ctx.UserAdminRepositoryMock.Setup(x => x.GetAdminSnapshotAsync(targetUserId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAdminSnapshot(targetUserId, "u@x.com", "U", IsActive: true, GcRecord: 0, HasMembership: true, StatusChangeReason: null));
        ctx.UserAdminRepositoryMock.Setup(x => x.SetUserActiveStatusAsync(targetUserId, false, "x", It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        ctx.PermissionServiceMock.Setup(x => x.InvalidateUserCacheAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        var response = await ctx.Handler.Handle(
            new ChangeUserStatusCommand(targetUserId, false, "x"),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenTenantIsEmpty_ShouldReturnTenantRequired()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());

        var response = await ctx.Handler.Handle(
            new ChangeUserStatusCommand(Guid.NewGuid(), true, "x"),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TENANT_REQUIRED");
    }

    private sealed class Context
    {
        public Mock<IUserAdminRepository> UserAdminRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IPermissionService> PermissionServiceMock { get; } = new();
        public Mock<IAuditLogger> AuditLoggerMock { get; } = new();
        public Mock<ILogger<ChangeUserStatusCommandHandler>> LoggerMock { get; } = new();

        public ChangeUserStatusCommandHandler Handler => new(
            UserAdminRepositoryMock.Object,
            CurrentUserServiceMock.Object,
            PermissionServiceMock.Object,
            AuditLoggerMock.Object,
            LoggerMock.Object);
    }
}
