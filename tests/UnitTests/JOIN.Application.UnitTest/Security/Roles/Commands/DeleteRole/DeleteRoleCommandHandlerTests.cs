using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Roles.Commands.DeleteRole;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using Microsoft.Extensions.Logging.Abstractions;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Moq;

namespace JOIN.Application.UnitTest.Security.Roles.Commands.DeleteRole;

/// <summary>
/// Tests for the role delete command handler. Covers the system-default guard, the missing-role branch, and the happy path.
/// </summary>
public sealed class DeleteRoleCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Empty CompanyId short-circuits the handler.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnError()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new DeleteRoleCommand(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// Role not found returns the canonical 404 message.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleMissing_ShouldReturnNotFoundMessage()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationRole?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new DeleteRoleCommand(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("Rol no encontrado o inactivo.");
        context.RoleRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// System-default role cannot be deleted; descriptive ≥50-char message.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleIsSystemDefault_ShouldReturnForbiddenWithoutUpdate()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationRole
            {
                Id = roleId,
                Name = "SuperAdmin",
                NormalizedName = "SUPERADMIN",
                IsSystemDefault = true,
                GcRecord = 0
            });

        var handler = context.CreateHandler();
        var response = await handler.Handle(new DeleteRoleCommand(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Length.Should().BeGreaterThanOrEqualTo(50);
        response.Message.Should().Contain("sistema");
        context.RoleRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// SaveChangesAsync returning 0 (race condition) produces a 404-flavored error.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnNotFoundMessage()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationRole
            {
                Id = roleId,
                Name = "Custom",
                NormalizedName = "CUSTOM",
                IsSystemDefault = false,
                GcRecord = 0
            });
        context.RoleRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new DeleteRoleCommand(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("Rol no encontrado o inactivo.");
    }

    /// <summary>
    /// Happy path: handler stamps GcRecord with the yyyyMMdd UTC int and persists through UpdateAsync + SaveChangesAsync.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleIsCustom_ShouldStampDeletionStampAndPersist()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationRole
            {
                Id = roleId,
                Name = "Custom",
                NormalizedName = "CUSTOM",
                IsSystemDefault = false,
                GcRecord = 0
            });
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        ApplicationRole? captured = null;
        context.RoleRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()))
            .Callback<ApplicationRole, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var before = DateTime.UtcNow;
        var handler = context.CreateHandler();
        var response = await handler.Handle(new DeleteRoleCommand(roleId), CancellationToken.None);
        var after = DateTime.UtcNow;

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.GcRecord.Should().BeGreaterThan(0);
        // The stamp must lie between the before/after UTC times the handler saw.
        var stampDate = ParseStamp(captured.GcRecord);
        stampDate.Date.Should().BeOnOrAfter(before.Date).And.BeOnOrBefore(after.Date);
        captured.LastModifiedBy.Should().Be("user-1");
        captured.LastModified.Should().NotBeNull();
    }

    private static DateTime ParseStamp(int yyyymmdd)
    {
        var s = yyyymmdd.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        return DateTime.SpecifyKind(
            new DateTime(int.Parse(s[..4]), int.Parse(s[4..6]), int.Parse(s[6..8])),
            DateTimeKind.Utc);
    }

    /// <summary>
    /// ROLE_HAS_USERS guard: a role with active assignments cannot be deleted. The handler must
    /// short-circuit before touching Update/SaveChanges.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleHasActiveUsers_ShouldReturnRoleHasUsersError()
    {
        var roleId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        context.SetupCustomRole(roleId);
        context.UnitOfWorkMock.SetupRepositoryRows<UserRoleCompany>(
        [
            new UserRoleCompany { RoleId = roleId, UserId = Guid.NewGuid(), CompanyId = companyId },
            new UserRoleCompany { RoleId = roleId, UserId = Guid.NewGuid(), CompanyId = companyId }
        ]);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new DeleteRoleCommand(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_HAS_USERS");
        response.Errors.Should().Contain(s => s.Contains("2 usuario(s)"));
        context.RoleRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Deleted assignments do not block: the happy path stays intact.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleHasZeroActiveUsers_ShouldProceedToSoftDelete()
    {
        var roleId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.SetupCustomRole(roleId);
        var removed = new UserRoleCompany { RoleId = roleId, UserId = Guid.NewGuid(), CompanyId = companyId };
        removed.MarkAsDeleted();
        context.UnitOfWorkMock.SetupRepositoryRows<UserRoleCompany>([removed]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new DeleteRoleCommand(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// SPEC 41 (decision 2026-10-08): a role is shared across companies, so users assigned in ANY company
    /// block the delete (before SPEC 41 only the caller's tenant was counted).
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleHasUsersInOtherTenant_ShouldReturnRoleHasUsersError()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.SetupCustomRole(roleId);
        context.UnitOfWorkMock.SetupRepositoryRows<UserRoleCompany>(
            [new UserRoleCompany { RoleId = roleId, UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid() }]);

        var response = await context.CreateHandler().Handle(new DeleteRoleCommand(roleId), CancellationToken.None);

        response.Message.Should().Be("ROLE_HAS_USERS");
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// SPEC 41 (decision 2026-10-08): the role's permissions and company links are composition and are
    /// soft-deleted with it, with the role's stamp.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleHasPermissionsAndCompanyLinks_ShouldCascadeTheSameStamp()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        var role = context.SetupCustomRole(roleId);
        var permission = new RoleSystemOption { RoleId = roleId, SystemOptionId = Guid.NewGuid(), CompanyId = Guid.NewGuid(), CanRead = true };
        var otherRolePermission = new RoleSystemOption { RoleId = Guid.NewGuid(), SystemOptionId = Guid.NewGuid(), CompanyId = Guid.NewGuid() };
        var link = new RoleCompany { RoleId = roleId, CompanyId = Guid.NewGuid() };
        var permissions = context.UnitOfWorkMock.SetupRepositoryRows<RoleSystemOption>([permission, otherRolePermission]);
        var links = context.UnitOfWorkMock.SetupRepositoryRows<RoleCompany>([link]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(new DeleteRoleCommand(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        role.GcRecord.Should().BeGreaterThan(0);
        permission.GcRecord.Should().Be(role.GcRecord);
        link.GcRecord.Should().Be(role.GcRecord);
        otherRolePermission.GcRecord.Should().Be(0, "only the deleted role's rows cascade");
        permissions.Verify(x => x.UpdateAsync(permission), Times.Once);
        links.Verify(x => x.UpdateAsync(link), Times.Once);
    }

    private sealed class TestContext
    {
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<IRoleRepository> RoleRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IAuditLogger> AuditLoggerMock { get; } = new();

        // Default: no active users — the auto-mocked UserRoleCompany repository returns no rows (SPEC 41).

        public ApplicationRole SetupCustomRole(Guid roleId)
        {
            var role = new ApplicationRole
            {
                Id = roleId,
                Name = "Custom",
                NormalizedName = "CUSTOM",
                IsSystemDefault = false,
                GcRecord = 0
            };
            RoleRepositoryMock.Setup(x => x.GetByIdForUpdateAsync(roleId, It.IsAny<CancellationToken>())).ReturnsAsync(role);
            return role;
        }

        public DeleteRoleCommandHandler CreateHandler()
        {
            return new DeleteRoleCommandHandler(
                UnitOfWorkMock.Object,
                RoleRepositoryMock.Object,
                CurrentUserServiceMock.Object,
                AuditLoggerMock.Object,
                NullLogger<DeleteRoleCommandHandler>.Instance);
        }
    }
}
