using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Security.Roles.Commands.RestoreRole;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.Security.Roles.Commands.RestoreRole;

/// <summary>
/// Unit tests for <see cref="RestoreRoleCommandHandler"/> (SPEC 41, Etapa 3).
/// </summary>
public sealed class RestoreRoleCommandHandlerTests
{
    private const int Stamp = 20260928;

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(companyId: Guid.Empty);

        var response = await context.CreateHandler().Handle(new RestoreRoleCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotSuperAdmin_ShouldReturnSuperAdminRequired()
    {
        var context = new TestContext(isSuperAdmin: false);
        var role = context.SetupRole(Stamp);

        var response = await context.CreateHandler().Handle(new RestoreRoleCommand(role.Id), CancellationToken.None);

        response.Message.Should().Be("SUPERADMIN_REQUIRED");
        role.GcRecord.Should().Be(Stamp);
    }

    [Fact]
    public async Task Handle_WhenRoleDoesNotExist_ShouldReturnNotFound()
    {
        var context = new TestContext();

        var response = await context.CreateHandler().Handle(new RestoreRoleCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenRoleIsActive_ShouldReturnNotDeleted()
    {
        var context = new TestContext();
        var role = context.SetupRole(BaseAuditableEntity.ActiveGcRecord);

        var response = await context.CreateHandler().Handle(new RestoreRoleCommand(role.Id), CancellationToken.None);

        response.Message.Should().Be("NOT_DELETED");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnRestoreFailed()
    {
        var context = new TestContext();
        var role = context.SetupRole(Stamp);
        context.SetupSaveChanges(0);

        var response = await context.CreateHandler().Handle(new RestoreRoleCommand(role.Id), CancellationToken.None);

        response.Message.Should().Be("RESTORE_FAILED");
    }

    [Fact]
    public async Task Handle_ShouldRestoreTheRoleAndTheChildrenOfItsCascade()
    {
        var context = new TestContext();
        var role = context.SetupRole(Stamp);
        context.SetupSaveChanges(1);
        var option = context.Active<SystemOption>();
        var company = context.Active<Company>();
        var deletedCompany = context.Deleted<Company>();
        var permission = new RoleSystemOption { RoleId = role.Id, SystemOptionId = option, CompanyId = company, GcRecord = Stamp };
        var olderPermission = new RoleSystemOption { RoleId = role.Id, SystemOptionId = option, CompanyId = company, GcRecord = 20260101 };
        var permissionInDeletedCompany = new RoleSystemOption { RoleId = role.Id, SystemOptionId = option, CompanyId = deletedCompany, GcRecord = Stamp };
        var link = new RoleCompany { RoleId = role.Id, CompanyId = company, GcRecord = Stamp };
        var linkInDeletedCompany = new RoleCompany { RoleId = role.Id, CompanyId = deletedCompany, GcRecord = Stamp };
        context.UnitOfWorkMock.SetupRepositoryRows<RoleSystemOption>([permission, olderPermission, permissionInDeletedCompany]);
        context.UnitOfWorkMock.SetupRepositoryRows<RoleCompany>([link, linkInDeletedCompany]);

        var response = await context.CreateHandler().Handle(new RestoreRoleCommand(role.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(role.Id);
        role.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
        permission.IsDeleted.Should().BeFalse();
        link.IsDeleted.Should().BeFalse();
        olderPermission.IsDeleted.Should().BeTrue("deleted before the role, in another cascade");
        permissionInDeletedCompany.IsDeleted.Should().BeTrue("a child never comes back under a deleted parent");
        linkInDeletedCompany.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenAnActiveLinkAlreadyExists_ShouldLeaveTheCascadedLinkDeleted()
    {
        var context = new TestContext();
        var role = context.SetupRole(Stamp);
        context.SetupSaveChanges(1);
        var company = context.Active<Company>();
        var cascaded = new RoleCompany { RoleId = role.Id, CompanyId = company, GcRecord = Stamp };
        var active = new RoleCompany { RoleId = role.Id, CompanyId = company };
        context.UnitOfWorkMock.SetupRepositoryRows<RoleCompany>([cascaded, active]);

        var response = await context.CreateHandler().Handle(new RestoreRoleCommand(role.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        cascaded.IsDeleted.Should().BeTrue();
    }

    private sealed class TestContext
    {
        public TestContext(Guid? companyId = null, bool isSuperAdmin = true)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId ?? Guid.NewGuid());
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationRole>()).Returns(RoleRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<ApplicationRole>> RoleRepositoryMock { get; } = new();

        public ApplicationRole SetupRole(int gcRecord)
        {
            var role = new ApplicationRole { Id = Guid.NewGuid(), Name = "Custom", GcRecord = gcRecord };
            RoleRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(role.Id)).ReturnsAsync(role);
            return role;
        }

        public Guid Active<T>()
            where T : BaseAuditableEntity, new()
            => Register<T>(deleted: false);

        public Guid Deleted<T>()
            where T : BaseAuditableEntity, new()
            => Register<T>(deleted: true);

        private readonly Dictionary<Type, Mock> _catalogRepositories = [];

        private Guid Register<T>(bool deleted)
            where T : BaseAuditableEntity, new()
        {
            var entity = new T();
            if (deleted)
            {
                entity.MarkAsDeleted();
            }

            if (!_catalogRepositories.TryGetValue(typeof(T), out var mock))
            {
                mock = new Mock<IGenericRepository<T>>();
                _catalogRepositories[typeof(T)] = mock;
                UnitOfWorkMock.Setup(x => x.GetRepository<T>()).Returns(((Mock<IGenericRepository<T>>)mock).Object);
            }

            ((Mock<IGenericRepository<T>>)mock).Setup(x => x.GetIncludingDeletedAsync(entity.Id)).ReturnsAsync(entity);
            return entity.Id;
        }

        public void SetupSaveChanges(int affectedRows)
            => UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(affectedRows);

        public RestoreRoleCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object, new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object));
    }
}
