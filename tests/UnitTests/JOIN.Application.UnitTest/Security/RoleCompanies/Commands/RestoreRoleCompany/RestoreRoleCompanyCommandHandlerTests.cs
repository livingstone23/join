using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Security.RoleCompanies.Commands.DeleteRoleCompany;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.Security.RoleCompanies.Commands.RestoreRoleCompany;

/// <summary>
/// Unit tests for <see cref="RestoreRoleCompanyCommandHandler"/> (SPEC 41).
/// </summary>
public sealed class RestoreRoleCompanyCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(companyId: Guid.Empty);

        var response = await context.CreateHandler().Handle(new RestoreRoleCompanyCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotSuperAdmin_ShouldReturnSuperAdminRequired()
    {
        var context = new TestContext(isSuperAdmin: false);
        var entity = context.SetupDeleted();

        var response = await context.CreateHandler().Handle(new RestoreRoleCompanyCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("SUPERADMIN_REQUIRED");
        entity.IsDeleted.Should().BeTrue();
        context.EntityRepositoryMock.Verify(x => x.GetIncludingDeletedAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEntityDoesNotExist_ShouldReturnNotFound()
    {
        var context = new TestContext();

        var response = await context.CreateHandler().Handle(new RestoreRoleCompanyCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenEntityIsActive_ShouldReturnNotDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        entity.Restore();

        var response = await context.CreateHandler().Handle(new RestoreRoleCompanyCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("NOT_DELETED");
    }

    [Fact]
    public async Task Handle_WhenChecksPass_ShouldRestore()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestoreRoleCompanyCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenParentApplicationRole0IsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Parent0.GcRecord = 20260928;

        var response = await context.CreateHandler().Handle(new RestoreRoleCompanyCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenParentCompany1IsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Parent1.MarkAsDeleted();

        var response = await context.CreateHandler().Handle(new RestoreRoleCompanyCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenActiveDuplicateExists_ShouldReturnActiveDuplicateExists()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        var duplicate = new RoleCompany { CompanyId = CompanyId, RoleId = context.Parent0.Id };
        context.EntityRepositoryMock.SetupRows([entity, duplicate]);

        var response = await context.CreateHandler().Handle(new RestoreRoleCompanyCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("ACTIVE_DUPLICATE_EXISTS");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenDuplicateIsAlsoDeleted_ShouldRestore()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        var duplicate = new RoleCompany { CompanyId = CompanyId, RoleId = context.Parent0.Id };
        duplicate.MarkAsDeleted();
        context.EntityRepositoryMock.SetupRows([entity, duplicate]);
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestoreRoleCompanyCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsDeleted.Should().BeFalse();
    }

    private static T Apply<T>(T entity, Action<T> mutate)
    {
        mutate(entity);
        return entity;
    }

    // The company parent must be the tenant of the row (CompanyId), so its id is forced.
    [Fact]
    public async Task Handle_WhenRestored_ShouldInvalidateThePermissionCacheOfTheRoleUsers()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.SetupSaveChanges(1);
        var holder = new UserRoleCompany { RoleId = entity.RoleId, CompanyId = entity.CompanyId, UserId = Guid.NewGuid() };
        var otherCompany = new UserRoleCompany { RoleId = entity.RoleId, CompanyId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        context.UnitOfWorkMock.SetupRepositoryRows<UserRoleCompany>([holder, otherCompany]);

        var response = await context.CreateHandler().Handle(new RestoreRoleCompanyCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.PermissionServiceMock.Verify(x => x.InvalidateUserCacheAsync(entity.CompanyId, holder.UserId, It.IsAny<CancellationToken>()), Times.Once);
        context.PermissionServiceMock.Verify(x => x.InvalidateUserCacheAsync(It.IsAny<Guid>(), otherCompany.UserId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRestoreFails_ShouldNotInvalidateTheCache()
    {
        var context = new TestContext(isSuperAdmin: true);

        await context.CreateHandler().Handle(new RestoreRoleCompanyCommand(Guid.NewGuid()), CancellationToken.None);

        context.PermissionServiceMock.Verify(x => x.InvalidateUserCacheAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Company WithId(Company company, Guid id)
    {
        typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(company, id);
        return company;
    }

    private sealed class TestContext
    {
        public TestContext(Guid? companyId = null, bool isSuperAdmin = true)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId ?? CompanyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            UnitOfWorkMock.Setup(x => x.GetRepository<RoleCompany>()).Returns(EntityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationRole>()).Returns(Parent0RepositoryMock.Object);
            Parent0RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent0.Id)).ReturnsAsync(Parent0);
            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(Parent1RepositoryMock.Object);
            Parent1RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent1.Id)).ReturnsAsync(Parent1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<RoleCompany>> EntityRepositoryMock { get; } = new();
        public ApplicationRole Parent0 { get; } = new ApplicationRole { Id = Guid.NewGuid(), Name = "Custom" };
        public Mock<IGenericRepository<ApplicationRole>> Parent0RepositoryMock { get; } = new();
        public Company Parent1 { get; } = WithId(new Company { Name = "JOIN", TaxId = "RUC-1" }, CompanyId);
        public Mock<IGenericRepository<Company>> Parent1RepositoryMock { get; } = new();

        public RoleCompany SetupDeleted()
        {
            var entity = new RoleCompany { CompanyId = CompanyId, RoleId = Parent0.Id };
            entity.MarkAsDeleted();
            EntityRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(entity.Id)).ReturnsAsync(entity);
            EntityRepositoryMock.SetupRows([entity]);
            return entity;
        }

        public void SetupSaveChanges(int affectedRows)
            => UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(affectedRows);

        public Mock<IPermissionService> PermissionServiceMock { get; } = new();

        public RestoreRoleCompanyCommandHandler CreateHandler()
            => new(
                CurrentUserServiceMock.Object,
                new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object),
                new RolePermissionCacheInvalidator(UnitOfWorkMock.Object, PermissionServiceMock.Object));
    }
}
