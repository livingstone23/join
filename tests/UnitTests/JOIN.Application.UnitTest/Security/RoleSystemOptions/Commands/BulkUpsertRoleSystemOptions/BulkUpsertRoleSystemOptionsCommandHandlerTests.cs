using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Commands.BulkUpsertRoleSystemOptions;
using JOIN.Domain.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JOIN.Application.UnitTest.Security.RoleSystemOptions.Commands.BulkUpsertRoleSystemOptions;

/// <summary>
/// Tests for the role-system-option bulk upsert handler (SPEC 25).
/// Verifies tenant check, role existence check, the diff contract
/// (created/updated/removed are forwarded as-is), and that cache invalidation
/// only fires for users actually assigned to the role in the caller's tenant.
/// </summary>
public sealed class BulkUpsertRoleSystemOptionsCommandHandlerTests
{
    private static UpsertRoleSystemOptionItemDto Item(Guid? optionId = null) => new(
        SystemOptionId: optionId ?? Guid.NewGuid(),
        CanRead: true, CanCreate: false, CanUpdate: false, CanDelete: false,
        CanDownload: false, CanExport: false, CanExecute: false);

    private static BulkUpsertRoleSystemOptionsCommand Cmd(Guid? roleId = null) => new(
        RoleId: roleId ?? Guid.NewGuid(),
        Items: new List<UpsertRoleSystemOptionItemDto>
        {
            Item(),
            Item()
        });

    /// <summary>
    /// Empty CompanyId short-circuits before the role check.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTenantEmpty_ShouldReturnTenantRequiredError()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var handler = context.CreateHandler();
        var response = await handler.Handle(Cmd(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TENANT_REQUIRED");
        context.RoleRepositoryMock.Verify(x => x.ExistsAndActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        context.RoleSystemOptionsRepositoryMock.Verify(x => x.BulkUpsertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<RoleSystemOption>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Missing or inactive role returns ROLE_NOT_FOUND without touching the bulk repo.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleNotFound_ShouldReturnRoleNotFoundError()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = context.CreateHandler();
        var response = await handler.Handle(Cmd(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_NOT_FOUND");
        context.RoleSystemOptionsRepositoryMock.Verify(x => x.BulkUpsertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<RoleSystemOption>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Happy path: the diff is forwarded verbatim and cache invalidation fires once per affected user.
    /// </summary>
    [Fact]
    public async Task Handle_WhenValid_ShouldCallBulkUpsertAndInvalidateCache()
    {
        var roleId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var createdId = Guid.NewGuid();
        var updatedId = Guid.NewGuid();
        var removedId = Guid.NewGuid();

        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.RoleRepositoryMock
            .Setup(x => x.GetActiveUserIdsByRoleIdAsync(roleId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { user1, user2 });
        context.RoleSystemOptionsRepositoryMock
            .Setup(x => x.BulkUpsertAsync(
                roleId, companyId,
                It.IsAny<IReadOnlyList<RoleSystemOption>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                new List<Guid> { createdId } as IReadOnlyList<Guid>,
                new List<Guid> { updatedId } as IReadOnlyList<Guid>,
                new List<Guid> { removedId } as IReadOnlyList<Guid>));

        var handler = context.CreateHandler();
        var response = await handler.Handle(Cmd(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Created.Should().BeEquivalentTo(new[] { createdId });
        response.Data.Updated.Should().BeEquivalentTo(new[] { updatedId });
        response.Data.Removed.Should().BeEquivalentTo(new[] { removedId });
        context.PermissionServiceMock.Verify(
            x => x.InvalidateUserCacheAsync(companyId, user1, It.IsAny<CancellationToken>()),
            Times.Once);
        context.PermissionServiceMock.Verify(
            x => x.InvalidateUserCacheAsync(companyId, user2, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Zero affected users → cache invalidation is a no-op (no error, no calls).
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoUsersInRole_ShouldNotCallInvalidate()
    {
        var roleId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.RoleRepositoryMock
            .Setup(x => x.GetActiveUserIdsByRoleIdAsync(roleId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());
        context.RoleSystemOptionsRepositoryMock
            .Setup(x => x.BulkUpsertAsync(
                roleId, companyId,
                It.IsAny<IReadOnlyList<RoleSystemOption>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                Array.Empty<Guid>() as IReadOnlyList<Guid>,
                Array.Empty<Guid>() as IReadOnlyList<Guid>,
                Array.Empty<Guid>() as IReadOnlyList<Guid>));

        var handler = context.CreateHandler();
        var response = await handler.Handle(Cmd(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.PermissionServiceMock.Verify(
            x => x.InvalidateUserCacheAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// If the bulk diff throws, the handler propagates — cache is NOT invalidated (consistency over freshness).
    /// </summary>
    [Fact]
    public async Task Handle_WhenBulkUpsertThrows_ShouldPropagateExceptionWithoutInvalidatingCache()
    {
        var roleId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.RoleSystemOptionsRepositoryMock
            .Setup(x => x.BulkUpsertAsync(
                roleId, companyId,
                It.IsAny<IReadOnlyList<RoleSystemOption>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated bulk failure"));

        var handler = context.CreateHandler();

        var act = () => handler.Handle(Cmd(roleId), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("simulated bulk failure");

        context.PermissionServiceMock.Verify(
            x => x.InvalidateUserCacheAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.RoleRepositoryMock.Verify(
            x => x.GetActiveUserIdsByRoleIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class TestContext
    {
        public Mock<IRoleSystemOptionsRepository> RoleSystemOptionsRepositoryMock { get; } = new();
        public Mock<IRoleRepository> RoleRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IPermissionService> PermissionServiceMock { get; } = new();
        public Mock<IAuditLogger> AuditLoggerMock { get; } = new();

        public BulkUpsertRoleSystemOptionsCommandHandler CreateHandler()
            => new(
                RoleSystemOptionsRepositoryMock.Object,
                RoleRepositoryMock.Object,
                CurrentUserServiceMock.Object,
                PermissionServiceMock.Object,
                AuditLoggerMock.Object,
                NullLogger<BulkUpsertRoleSystemOptionsCommandHandler>.Instance);
    }
}
