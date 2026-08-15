using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security;
using JOIN.Application.UseCases.Security.Roles.Commands.CreateRole;
using JOIN.Domain.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JOIN.Application.UnitTest.Security.Roles.Commands.CreateRole;

/// <summary>
/// Tests for the role creation command handler.
/// Covers tenant validation, duplicate name detection, persistence failure, the happy path,
/// and the CloneFromRoleId path (SPEC 24).
/// </summary>
public sealed class CreateRoleCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Empty CompanyId short-circuits before the repository is touched.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnErrorWithoutTouchingRepo()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new CreateRoleCommand("Admin", null, false), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        context.RoleRepositoryMock.Verify(x => x.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        context.RoleRepositoryMock.Verify(x => x.AddAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Duplicate name returns the canonical conflict message and skips AddAsync.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ShouldReturnConflictWithoutInsert()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.ExistsByNameAsync("ADMIN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new CreateRoleCommand("Admin", null, false), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().StartWith("Ya existe un rol con el nombre");
        context.RoleRepositoryMock.Verify(x => x.AddAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// SaveChangesAsync returning 0 on the role insert produces an error response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnError()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock.Setup(x => x.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new CreateRoleCommand("Admin", null, false), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// Happy path: the role is created and the returned DTO carries the trimmed/normalized values.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateRoleAndReturnDto()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.RoleRepositoryMock.Setup(x => x.ExistsByNameAsync("ADMIN", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        ApplicationRole? captured = null;
        context.RoleRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()))
            .Callback<ApplicationRole, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var mapperMock = new Mock<IRoleMapper>();
        var dto = new RoleDto(Guid.NewGuid(), "Admin", "ADMIN", "All access", false, "user-1", DateTime.UtcNow);
        mapperMock.Setup(x => x.FromEntity(It.IsAny<ApplicationRole>())).Returns(dto);

        var handler = new CreateRoleCommandHandler(
            context.UnitOfWorkMock.Object,
            context.RoleRepositoryMock.Object,
            mapperMock.Object,
            context.CurrentUserServiceMock.Object,
            NullLogger<CreateRoleCommandHandler>.Instance);

        var response = await handler.Handle(new CreateRoleCommand("  Admin  ", "All access", false), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Name.Should().Be("Admin");
        captured.NormalizedName.Should().Be("ADMIN");
        captured.Description.Should().Be("All access");
        captured.CreatedBy.Should().Be("user-1");
        response.Data.Should().Be(dto);
    }

    /// <summary>
    /// CloneFromRoleId == null: behavior is identical to the happy path. No permission inserts happen.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCloneFromRoleIdIsNull_ShouldSkipCloning()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.RoleRepositoryMock.Setup(x => x.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new CreateRoleCommand("Admin", null, false, CloneFromRoleId: null), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.RoleRepositoryMock.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.RoleSystemOptions.GetActiveByRoleAndCompanyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// CloneFromRoleId points to an existing role with 2 active permissions in the caller's tenant:
    /// the new role gets created AND the 2 RoleSystemOption rows are inserted in the same tx.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCloneFromRoleIdProvidedAndOriginHasPermissions_ShouldInsertCopies()
    {
        var tenantId = Guid.NewGuid();
        var originId = Guid.NewGuid();
        var newRoleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.RoleRepositoryMock.Setup(x => x.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Origin role exists, has 2 permissions in the caller's tenant.
        var originRoleDto = new RoleDto(originId, "Origin", "ORIGIN", null, false, "seed", DateTime.UtcNow, PermissionsCount: 2);
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdAsync(originId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originRoleDto);

        var originPermissions = new List<RoleSystemOption>
        {
            new() { RoleId = originId, SystemOptionId = Guid.NewGuid(), CompanyId = tenantId, CanRead = true, CanCreate = true, GcRecord = 0, OrderMenu = 1, IsVisibleMenu = true },
            new() { RoleId = originId, SystemOptionId = Guid.NewGuid(), CompanyId = tenantId, CanRead = true, CanUpdate = true, CanExport = true, GcRecord = 0, OrderMenu = 2, IsVisibleMenu = false }
        };
        context.RoleSystemOptionsRepositoryMock
            .Setup(x => x.GetActiveByRoleAndCompanyAsync(originId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originPermissions);

        // The first SaveChangesAsync returns the new role row count; subsequent calls (for clone)
        // return the cloned permission row count.
        var saveCallCount = 0;
        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++saveCallCount == 1 ? 1 : originPermissions.Count);

        ApplicationRole? capturedRole = null;
        context.RoleRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()))
            .Callback<ApplicationRole, CancellationToken>((r, _) =>
            {
                capturedRole = r;
                // Simulate EF identity assignment via reflection: BaseEntity.Id has a protected setter.
                var idProp = typeof(ApplicationRole).GetProperty("Id");
                idProp!.SetValue(r, newRoleId);
            })
            .Returns(Task.CompletedTask);

        var capturedPermissions = new List<RoleSystemOption>();
        context.RoleSystemOptionsRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<RoleSystemOption>()))
            .Callback<RoleSystemOption>((p) => capturedPermissions.Add(p))
            .ReturnsAsync(true);

        var mapperMock = new Mock<IRoleMapper>();
        mapperMock.Setup(x => x.FromEntity(It.IsAny<ApplicationRole>())).Returns((ApplicationRole r) => new RoleDto(r.Id, r.Name, r.NormalizedName, r.Description, r.IsSystemDefault, r.CreatedBy, r.Created));

        var handler = new CreateRoleCommandHandler(
            context.UnitOfWorkMock.Object,
            context.RoleRepositoryMock.Object,
            mapperMock.Object,
            context.CurrentUserServiceMock.Object,
            NullLogger<CreateRoleCommandHandler>.Instance);

        var response = await handler.Handle(new CreateRoleCommand("Cloned", null, false, CloneFromRoleId: originId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        capturedRole.Should().NotBeNull();
        capturedPermissions.Should().HaveCount(2);
        capturedPermissions.Should().AllSatisfy(p =>
        {
            p.RoleId.Should().Be(newRoleId);
            p.CompanyId.Should().Be(tenantId);
            p.CreatedBy.Should().Be("user-1");
            p.GcRecord.Should().Be(0);
        });
        // Verify flag fidelity: the cloned rows mirror the origin's flags.
        capturedPermissions[0].SystemOptionId.Should().Be(originPermissions[0].SystemOptionId);
        capturedPermissions[0].CanRead.Should().BeTrue();
        capturedPermissions[0].CanCreate.Should().BeTrue();
        capturedPermissions[0].OrderMenu.Should().Be(1);
        capturedPermissions[0].IsVisibleMenu.Should().BeTrue();
        capturedPermissions[1].CanExport.Should().BeTrue();
        capturedPermissions[1].OrderMenu.Should().Be(2);
    }

    /// <summary>
    /// CloneFromRoleId points to a missing or soft-deleted role → ROLE_NOT_FOUND before any insert.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCloneFromRoleIdProvidedButOriginMissing_ShouldReturnRoleNotFound()
    {
        var tenantId = Guid.NewGuid();
        var originId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleRepositoryMock.Setup(x => x.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdAsync(originId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleDto?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new CreateRoleCommand("Cloned", null, false, CloneFromRoleId: originId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_NOT_FOUND");
        context.RoleRepositoryMock.Verify(x => x.AddAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// CloneFromRoleId points to a role that has permissions in another tenant but none in the caller's
    /// tenant (PermissionsCount in caller's tenant == 0, but the role exists) → ROLE_NOT_FOUND (defense
    /// against silently cloning an empty role when the operator likely meant a cross-tenant reference).
    /// </summary>
    [Fact]
    public async Task Handle_WhenCloneFromRoleIdProvidedButOriginHasPermissionsOnlyInOtherTenant_ShouldReturnRoleNotFound()
    {
        var tenantId = Guid.NewGuid();
        var originId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleRepositoryMock.Setup(x => x.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Origin DTO reports PermissionsCount > 0 in another tenant; caller's tenant count is 0
        // (the DTO is projected with the caller's CompanyId filter).
        var originRoleDto = new RoleDto(originId, "Origin", "ORIGIN", null, false, "seed", DateTime.UtcNow, PermissionsCount: 5);
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdAsync(originId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originRoleDto);
        context.RoleSystemOptionsRepositoryMock
            .Setup(x => x.GetActiveByRoleAndCompanyAsync(originId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RoleSystemOption>());

        var handler = context.CreateHandler();
        var response = await handler.Handle(new CreateRoleCommand("Cloned", null, false, CloneFromRoleId: originId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_NOT_FOUND");
        context.RoleRepositoryMock.Verify(x => x.AddAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// CloneFromRoleId points to a role that exists but genuinely has no permissions in the caller's
    /// tenant → role is created with zero permissions. Not an error.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCloneFromRoleIdProvidedButOriginHasNoPermissions_ShouldCreateRoleWithoutPermissions()
    {
        var tenantId = Guid.NewGuid();
        var originId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.RoleRepositoryMock.Setup(x => x.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var originRoleDto = new RoleDto(originId, "Empty", "EMPTY", null, false, "seed", DateTime.UtcNow, PermissionsCount: 0);
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdAsync(originId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originRoleDto);
        context.RoleSystemOptionsRepositoryMock
            .Setup(x => x.GetActiveByRoleAndCompanyAsync(originId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RoleSystemOption>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var mapperMock = new Mock<IRoleMapper>();
        mapperMock.Setup(x => x.FromEntity(It.IsAny<ApplicationRole>())).Returns((ApplicationRole r) => new RoleDto(r.Id, r.Name, r.NormalizedName, r.Description, r.IsSystemDefault, r.CreatedBy, r.Created));

        var handler = new CreateRoleCommandHandler(
            context.UnitOfWorkMock.Object,
            context.RoleRepositoryMock.Object,
            mapperMock.Object,
            context.CurrentUserServiceMock.Object,
            NullLogger<CreateRoleCommandHandler>.Instance);

        var response = await handler.Handle(new CreateRoleCommand("Cloned", null, false, CloneFromRoleId: originId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.RoleSystemOptionsRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<RoleSystemOption>()), Times.Never);
    }

    /// <summary>
    /// Clone path: the role insert succeeds but the cloned RoleSystemOption rows report 0 affected.
    /// The handler returns ROLE_CLONE_FAILED instead of a successful response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCloneInsertsReturnZeroAffected_ShouldReturnRoleCloneFailed()
    {
        var tenantId = Guid.NewGuid();
        var originId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.RoleRepositoryMock.Setup(x => x.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var originRoleDto = new RoleDto(originId, "Origin", "ORIGIN", null, false, "seed", DateTime.UtcNow, PermissionsCount: 1);
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdAsync(originId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originRoleDto);

        var originPermissions = new List<RoleSystemOption>
        {
            new() { RoleId = originId, SystemOptionId = Guid.NewGuid(), CompanyId = tenantId, CanRead = true, GcRecord = 0 }
        };
        context.RoleSystemOptionsRepositoryMock
            .Setup(x => x.GetActiveByRoleAndCompanyAsync(originId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originPermissions);

        // First SaveChangesAsync (role insert) succeeds; second one (clone inserts) returns 0.
        var saveCallCount = 0;
        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++saveCallCount == 1 ? 1 : 0);

        var handler = new CreateRoleCommandHandler(
            context.UnitOfWorkMock.Object,
            context.RoleRepositoryMock.Object,
            new Mock<IRoleMapper>().Object,
            context.CurrentUserServiceMock.Object,
            NullLogger<CreateRoleCommandHandler>.Instance);

        var response = await handler.Handle(new CreateRoleCommand("Cloned", null, false, CloneFromRoleId: originId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_CLONE_FAILED");
    }

    private sealed class TestContext
    {
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IRoleRepository> RoleRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IRoleSystemOptionsRepository> RoleSystemOptionsRepositoryMock { get; } = new();

        public TestContext()
        {
            // Wire the UoW.RoleSystemOptions property to the role-system-options mock so the
            // handler's unitOfWork.RoleSystemOptions.GetActiveByRoleAndCompanyAsync(...) resolves.
            UnitOfWorkMock.SetupGet(x => x.RoleSystemOptions).Returns(RoleSystemOptionsRepositoryMock.Object);
        }

        public CreateRoleCommandHandler CreateHandler()
        {
            var mapperMock = new Mock<IRoleMapper>();
            return new CreateRoleCommandHandler(
                UnitOfWorkMock.Object,
                RoleRepositoryMock.Object,
                mapperMock.Object,
                CurrentUserServiceMock.Object,
                NullLogger<CreateRoleCommandHandler>.Instance);
        }
    }
}