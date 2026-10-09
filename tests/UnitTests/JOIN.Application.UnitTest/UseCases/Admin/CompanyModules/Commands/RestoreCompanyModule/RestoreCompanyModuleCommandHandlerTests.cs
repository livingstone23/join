using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Admin.CompanyModules.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.CompanyModules.Commands.RestoreCompanyModule;

/// <summary>
/// Unit tests for <see cref="RestoreCompanyModuleCommandHandler"/> (SPEC 41).
/// </summary>
public sealed class RestoreCompanyModuleCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(companyId: Guid.Empty);

        var response = await context.CreateHandler().Handle(new RestoreCompanyModuleCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotSuperAdmin_ShouldReturnSuperAdminRequired()
    {
        var context = new TestContext(isSuperAdmin: false);
        var entity = context.SetupDeleted();

        var response = await context.CreateHandler().Handle(new RestoreCompanyModuleCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("SUPERADMIN_REQUIRED");
        entity.IsDeleted.Should().BeTrue();
        context.EntityRepositoryMock.Verify(x => x.GetIncludingDeletedAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEntityDoesNotExist_ShouldReturnNotFound()
    {
        var context = new TestContext();

        var response = await context.CreateHandler().Handle(new RestoreCompanyModuleCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenEntityIsActive_ShouldReturnNotDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        entity.Restore();

        var response = await context.CreateHandler().Handle(new RestoreCompanyModuleCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("NOT_DELETED");
    }

    [Fact]
    public async Task Handle_WhenChecksPass_ShouldRestore()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestoreCompanyModuleCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenParentCompany0IsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Parent0.MarkAsDeleted();

        var response = await context.CreateHandler().Handle(new RestoreCompanyModuleCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenParentSystemModule1IsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Parent1.MarkAsDeleted();

        var response = await context.CreateHandler().Handle(new RestoreCompanyModuleCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    private static T Apply<T>(T entity, Action<T> mutate)
    {
        mutate(entity);
        return entity;
    }

    // The company parent must be the tenant of the row (CompanyId), so its id is forced.
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
            UnitOfWorkMock.Setup(x => x.GetRepository<CompanyModule>()).Returns(EntityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(Parent0RepositoryMock.Object);
            Parent0RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent0.Id)).ReturnsAsync(Parent0);
            UnitOfWorkMock.Setup(x => x.GetRepository<SystemModule>()).Returns(Parent1RepositoryMock.Object);
            Parent1RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent1.Id)).ReturnsAsync(Parent1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<CompanyModule>> EntityRepositoryMock { get; } = new();
        public Company Parent0 { get; } = WithId(new Company { Name = "JOIN", TaxId = "RUC-1" }, CompanyId);
        public Mock<IGenericRepository<Company>> Parent0RepositoryMock { get; } = new();
        public SystemModule Parent1 { get; } = new SystemModule { Name = "Admin" };
        public Mock<IGenericRepository<SystemModule>> Parent1RepositoryMock { get; } = new();

        public CompanyModule SetupDeleted()
        {
            var entity = new CompanyModule { CompanyId = CompanyId, ModuleId = Parent1.Id };
            entity.MarkAsDeleted();
            EntityRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(entity.Id)).ReturnsAsync(entity);
            EntityRepositoryMock.SetupRows([entity]);
            return entity;
        }

        public void SetupSaveChanges(int affectedRows)
            => UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(affectedRows);

        public RestoreCompanyModuleCommandHandler CreateHandler()
            => new(CurrentUserServiceMock.Object, new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object));
    }
}
