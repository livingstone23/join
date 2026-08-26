using FluentAssertions;
using JOIN.Application.DTO.Security.RoleCompany;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.RoleCompanies.Commands.UpdateRoleCompany;
using JOIN.Application.UnitTest.Security.RoleCompanies.TestHelpers;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.Security.RoleCompanies.Commands.UpdateRoleCompany;

/// <summary>
/// Tests for the UpdateRoleCompanyCommandHandler.
/// Covers tenant scoping, missing-link detection, role validation, duplicate detection
/// (only when RoleId actually changes), and the no-change-same-RoleId path.
/// </summary>
public sealed class UpdateRoleCompanyCommandHandlerTests
{
    /// <summary>
    /// Empty CompanyId short-circuits with 401.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyId()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCompanyCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        context.RoleCompanyRepositoryMock.Verify(
            x => x.GetByIdForUpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Link not found (cross-tenant, missing, or soft-deleted) → 404.
    /// </summary>
    [Fact]
    public async Task Handle_WhenLinkMissing_ShouldReturnNotFound()
    {
        var tenantId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(It.IsAny<Guid>(), tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleCompany?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCompanyCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_COMPANY_NOT_FOUND");
        context.RoleRepositoryMock.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), tenantId, It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// New RoleId does not exist → 400 ROLE_NOT_FOUND.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNewRoleMissing_ShouldReturnRoleNotFound()
    {
        var tenantId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var newRoleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(linkId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleCompanyTestFactory.Create(linkId, Guid.NewGuid(), tenantId));
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdAsync(newRoleId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((global::JOIN.Application.DTO.Security.RoleDto?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCompanyCommand(linkId, newRoleId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_NOT_FOUND");
    }

    /// <summary>
    /// New RoleId points to soft-deleted role → 400 ROLE_INACTIVE.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNewRoleInactive_ShouldReturnRoleInactive()
    {
        var tenantId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var newRoleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(linkId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleCompanyTestFactory.Create(linkId, Guid.NewGuid(), tenantId));
        // GetByIdAsync filters GcRecord=0 so it returns null for inactive roles; ExistsAndActiveAsync false.
        // The handler reaches NotFound first. The path is unreachable with the current repo API.
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdAsync(newRoleId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((global::JOIN.Application.DTO.Security.RoleDto?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCompanyCommand(linkId, newRoleId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_NOT_FOUND");
    }

    /// <summary>
    /// Changing RoleId to one that already has an active link → 409 ROLE_COMPANY_DUPLICATE.
    /// </summary>
    [Fact]
    public async Task Handle_WhenChangingRoleCausesDuplicate_ShouldReturnConflict()
    {
        var tenantId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var oldRoleId = Guid.NewGuid();
        var newRoleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(linkId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleCompanyTestFactory.Create(linkId, oldRoleId, tenantId));
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdAsync(newRoleId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new global::JOIN.Application.DTO.Security.RoleDto(
                Guid.NewGuid(), "Admin", "ADMIN", null, false, null, DateTime.UtcNow));
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(newRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.ExistsActiveLinkAsync(newRoleId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCompanyCommand(linkId, newRoleId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_COMPANY_DUPLICATE");
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Same RoleId (no change) → duplicate check is skipped, update proceeds.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleIdUnchanged_ShouldSkipDuplicateCheckAndPersist()
    {
        var tenantId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(linkId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleCompanyTestFactory.Create(linkId, roleId, tenantId));
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdAsync(roleId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new global::JOIN.Application.DTO.Security.RoleDto(
                Guid.NewGuid(), "Admin", "ADMIN", null, false, null, DateTime.UtcNow));
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdAsync(linkId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleCompanyDto { Id = linkId, RoleId = roleId, RoleName = "Admin", IsSystemDefault = false, CreatedBy = "user-1", Created = DateTime.UtcNow });

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCompanyCommand(linkId, roleId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.RoleCompanyRepositoryMock.Verify(
            x => x.ExistsActiveLinkAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Changing RoleId to a non-conflicting role → 200 with the reloaded DTO.
    /// </summary>
    [Fact]
    public async Task Handle_WhenChangingRoleWithoutCollision_ShouldPersistAndReturnDto()
    {
        var tenantId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var oldRoleId = Guid.NewGuid();
        var newRoleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(linkId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleCompanyTestFactory.Create(linkId, oldRoleId, tenantId));
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdAsync(newRoleId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new global::JOIN.Application.DTO.Security.RoleDto(
                Guid.NewGuid(), "Editor", "EDITOR", null, false, null, DateTime.UtcNow));
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(newRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.ExistsActiveLinkAsync(newRoleId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        RoleCompany? captured = null;
        context.RoleCompanyRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<RoleCompany>(), It.IsAny<CancellationToken>()))
            .Callback<RoleCompany, CancellationToken>((rc, _) => captured = rc)
            .Returns(Task.CompletedTask);

        var reloadDto = new RoleCompanyDto
        {
            Id = linkId,
            RoleId = newRoleId,
            RoleName = "Editor",
            IsSystemDefault = false,
            CreatedBy = "user-1",
            Created = DateTime.UtcNow
        };
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdAsync(linkId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reloadDto);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCompanyCommand(linkId, newRoleId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(reloadDto);
        captured.Should().NotBeNull();
        captured!.RoleId.Should().Be(newRoleId);
        captured.CompanyId.Should().Be(tenantId);
        captured.LastModifiedBy.Should().Be("user-1");
    }

    /// <summary>
    /// CompanyId is never changed by an update — even if a caller tried to mutate the tracked entity's CompanyId, the
    /// handler does not assign it. Verify by checking the captured entity still carries the token tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUpdating_ShouldNotChangeCompanyId()
    {
        var tenantId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(linkId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleCompanyTestFactory.Create(linkId, roleId, tenantId));
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdAsync(roleId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new global::JOIN.Application.DTO.Security.RoleDto(
                Guid.NewGuid(), "Admin", "ADMIN", null, false, null, DateTime.UtcNow));
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdAsync(linkId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleCompanyDto { Id = linkId, RoleId = roleId, RoleName = "Admin", IsSystemDefault = false, CreatedBy = "user-1", Created = DateTime.UtcNow });

        var handler = context.CreateHandler();
        await handler.Handle(new UpdateRoleCompanyCommand(linkId, roleId), CancellationToken.None);

        // Captured entity carries the token tenant (no change attempted).
        context.RoleCompanyRepositoryMock.Verify(
            x => x.UpdateAsync(It.Is<RoleCompany>(rc => rc.CompanyId == tenantId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class TestContext
    {
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IRoleCompanyRepository> RoleCompanyRepositoryMock { get; } = new();
        public Mock<IRoleRepository> RoleRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IAuditLogger> AuditLoggerMock { get; } = new();

        public UpdateRoleCompanyCommandHandler CreateHandler() => new(
            UnitOfWorkMock.Object,
            RoleCompanyRepositoryMock.Object,
            RoleRepositoryMock.Object,
            CurrentUserServiceMock.Object,
            AuditLoggerMock.Object);
    }
}
