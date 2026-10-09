using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Security.UserCompanies.Commands.RestoreUserCompany;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.UserCompanies.Commands.RestoreUserCompany;

/// <summary>
/// Unit tests for <see cref="RestoreUserCompanyCommandHandler"/> (SPEC 41, Etapa 3).
/// </summary>
public sealed class RestoreUserCompanyCommandHandlerTests
{
    private static readonly DateTime RemovedOn = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCallerIsNotSuperAdmin_ShouldReturnSuperAdminRequired()
    {
        var context = new TestContext(isSuperAdmin: false);

        var response = await context.CreateHandler().Handle(new RestoreUserCompanyCommand(UserId, CompanyId), CancellationToken.None);

        response.Message.Should().Be("SUPERADMIN_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(tokenCompanyId: Guid.Empty);

        var response = await context.CreateHandler().Handle(new RestoreUserCompanyCommand(UserId, CompanyId), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenMembershipDoesNotExist_ShouldReturnNotFound()
    {
        var context = new TestContext();

        var response = await context.CreateHandler().Handle(new RestoreUserCompanyCommand(UserId, CompanyId), CancellationToken.None);

        response.Message.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenCompanyIsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        context.SetupMembership(isDefault: false);
        context.SetupCompany(deleted: true);

        var response = await context.CreateHandler().Handle(new RestoreUserCompanyCommand(UserId, CompanyId), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        context.PermissionServiceMock.Verify(x => x.InvalidateUserCacheAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ShouldRestoreTheMembershipItsRolesAndDropTheCache()
    {
        var context = new TestContext();
        var membership = context.SetupMembership(isDefault: true);
        context.SetupCompany(deleted: false);
        context.SetupSaveChanges(1);
        var activeRole = context.SetupRole(deleted: false);
        var deletedRole = context.SetupRole(deleted: true);
        var assignment = context.Assignment(activeRole, RemovedOn);
        var assignmentOfDeletedRole = context.Assignment(deletedRole, RemovedOn);
        var olderAssignment = context.Assignment(activeRole, RemovedOn.AddDays(-5));
        context.UnitOfWorkMock.SetupRepositoryRows<UserRoleCompany>([assignment, assignmentOfDeletedRole, olderAssignment]);

        var response = await context.CreateHandler().Handle(new RestoreUserCompanyCommand(UserId, CompanyId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        membership.IsDeleted.Should().BeFalse();
        membership.IsDefault.Should().BeTrue("no other default company is active");
        assignment.IsDeleted.Should().BeFalse();
        assignmentOfDeletedRole.IsDeleted.Should().BeTrue();
        olderAssignment.IsDeleted.Should().BeTrue("removed in another cascade");
        context.PermissionServiceMock.Verify(x => x.InvalidateUserCacheAsync(CompanyId, UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAnotherDefaultCompanyIsActive_ShouldRestoreWithoutIsDefault()
    {
        var context = new TestContext();
        var membership = context.SetupMembership(isDefault: true);
        context.SetupCompany(deleted: false);
        context.SetupSaveChanges(1);
        var otherDefault = new UserCompany { UserId = UserId, CompanyId = Guid.NewGuid(), IsDefault = true };
        context.MembershipRepositoryMock.SetupRows([membership, otherDefault]);

        var response = await context.CreateHandler().Handle(new RestoreUserCompanyCommand(UserId, CompanyId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        membership.IsDefault.Should().BeFalse();
    }

    private sealed class TestContext
    {
        public TestContext(Guid? tokenCompanyId = null, bool isSuperAdmin = true)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tokenCompanyId ?? Guid.NewGuid());
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            UnitOfWorkMock.Setup(x => x.GetRepository<UserCompany>()).Returns(MembershipRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationRole>()).Returns(RoleRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IPermissionService> PermissionServiceMock { get; } = new();
        public Mock<IGenericRepository<UserCompany>> MembershipRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationRole>> RoleRepositoryMock { get; } = new();

        public UserCompany SetupMembership(bool isDefault)
        {
            var membership = new UserCompany { UserId = UserId, CompanyId = CompanyId, IsDefault = isDefault };
            membership.MarkAsDeleted(RemovedOn);
            MembershipRepositoryMock.SetupRows([membership]);
            MembershipRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(membership.Id)).ReturnsAsync(membership);
            return membership;
        }

        public void SetupCompany(bool deleted)
        {
            var company = new Company { Name = "JOIN", TaxId = "RUC-1" };
            typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(company, CompanyId);
            if (deleted)
            {
                company.MarkAsDeleted();
            }

            var companyRepositoryMock = new Mock<IGenericRepository<Company>>();
            companyRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(CompanyId)).ReturnsAsync(company);
            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(companyRepositoryMock.Object);
        }

        public Guid SetupRole(bool deleted)
        {
            var role = new ApplicationRole { Id = Guid.NewGuid(), Name = "Role", GcRecord = deleted ? 20260101 : 0 };
            RoleRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(role.Id)).ReturnsAsync(role);
            return role.Id;
        }

        public UserRoleCompany Assignment(Guid roleId, DateTime removedOn)
        {
            var assignment = new UserRoleCompany { UserId = UserId, CompanyId = CompanyId, RoleId = roleId };
            assignment.MarkAsDeleted(removedOn);
            return assignment;
        }

        public void SetupSaveChanges(int affectedRows)
            => UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(affectedRows);

        public RestoreUserCompanyCommandHandler CreateHandler()
        {
            var restorer = new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
            return new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object, restorer, PermissionServiceMock.Object);
        }
    }
}
