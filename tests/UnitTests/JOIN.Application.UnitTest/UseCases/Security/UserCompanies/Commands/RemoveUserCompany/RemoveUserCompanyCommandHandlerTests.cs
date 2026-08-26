using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.UserCompanies.Commands.RemoveUserCompany;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.UserCompanies.Commands.RemoveUserCompany;

/// <summary>
/// SPEC 28 / F4 acceptance criteria for <see cref="RemoveUserCompanyCommandHandler"/>:
/// guard order (last-company beats default-company), soft-delete via
/// <c>MarkAsDeleted</c>, and the cache-invalidation contract.
/// </summary>
public sealed class RemoveUserCompanyCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenMembershipDoesNotExist_ShouldReturnMembershipNotFound()
    {
        var ctx = new Context();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        ctx.UserAdminRepositoryMock
            .Setup(x => x.GetMembershipInfoAsync(userId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserCompanyMembershipInfo?)null);

        var response = await ctx.Handler.Handle(
            new RemoveUserCompanyCommand(userId, companyId),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MEMBERSHIP_NOT_FOUND");
        ctx.PermissionServiceMock.Verify(
            x => x.InvalidateUserCacheAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMembershipExistsFalse_ShouldReturnMembershipNotFound()
    {
        var ctx = new Context();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        ctx.UserAdminRepositoryMock
            .Setup(x => x.GetMembershipInfoAsync(userId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserCompanyMembershipInfo(Exists: false, IsDefault: false, TotalActiveCompanies: 0));

        var response = await ctx.Handler.Handle(
            new RemoveUserCompanyCommand(userId, companyId),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MEMBERSHIP_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenUserHasOnlyOneCompany_ShouldReturnCannotRemoveLastCompany()
    {
        var ctx = new Context();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        ctx.UserAdminRepositoryMock
            .Setup(x => x.GetMembershipInfoAsync(userId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserCompanyMembershipInfo(Exists: true, IsDefault: true, TotalActiveCompanies: 1));

        var response = await ctx.Handler.Handle(
            new RemoveUserCompanyCommand(userId, companyId),
            CancellationToken.None);

        // Order matters: last-company error takes precedence over default-company.
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CANNOT_REMOVE_LAST_COMPANY");
        ctx.PermissionServiceMock.Verify(
            x => x.InvalidateUserCacheAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMembershipIsDefaultAndOtherCompaniesExist_ShouldReturnCannotRemoveDefaultCompany()
    {
        var ctx = new Context();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        ctx.UserAdminRepositoryMock
            .Setup(x => x.GetMembershipInfoAsync(userId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserCompanyMembershipInfo(Exists: true, IsDefault: true, TotalActiveCompanies: 3));

        var response = await ctx.Handler.Handle(
            new RemoveUserCompanyCommand(userId, companyId),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CANNOT_REMOVE_DEFAULT_COMPANY");
    }

    [Fact]
    public async Task Handle_WhenMembershipIsNotDefaultAndOtherCompaniesExist_ShouldSoftDeleteAndInvalidateCache()
    {
        var ctx = new Context();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        ctx.UserAdminRepositoryMock
            .Setup(x => x.GetMembershipInfoAsync(userId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserCompanyMembershipInfo(Exists: true, IsDefault: false, TotalActiveCompanies: 2));

        // The lookup of the active UserCompany row + the role index scan both
        // come from the same connection.
        ctx.ConnectionFactory.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = membershipId }),
            FakeResultSet.FromRows());

        // Use a fresh entity and let BaseEntity's ctor assign the Id — then mock
        // the repository to return it on GetAsync(membershipId).
        var userCompany = new UserCompany { GcRecord = 0 };
        var userCompanyRepo = new Mock<IGenericRepository<UserCompany>>();
        userCompanyRepo.Setup(x => x.GetAsync(membershipId)).ReturnsAsync(userCompany);
        ctx.UnitOfWorkMock.Setup(x => x.GetRepository<UserCompany>()).Returns(userCompanyRepo.Object);
        ctx.UnitOfWorkMock.Setup(x => x.GetRepository<UserRoleCompany>())
            .Returns(Mock.Of<IGenericRepository<UserRoleCompany>>());

        var response = await ctx.Handler.Handle(
            new RemoveUserCompanyCommand(userId, companyId),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        userCompany.GcRecord.Should().NotBe(0); // MarkAsDeleted stamps a non-zero yyyyMMdd value
        ctx.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        ctx.PermissionServiceMock.Verify(
            x => x.InvalidateUserCacheAsync(companyId, userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class Context
    {
        public FakeSqlConnectionFactory ConnectionFactory { get; } = new();
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IUserAdminRepository> UserAdminRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IPermissionService> PermissionServiceMock { get; } = new();
        public Mock<IAuditLogger> AuditLoggerMock { get; } = new();

        public Context()
        {
            UnitOfWorkMock
                .Setup(x => x.GetRepository<UserCompany>())
                .Returns(() => Mock.Of<IGenericRepository<UserCompany>>());
            UnitOfWorkMock
                .Setup(x => x.GetRepository<UserRoleCompany>())
                .Returns(() => Mock.Of<IGenericRepository<UserRoleCompany>>());
        }

        public RemoveUserCompanyCommandHandler Handler => new(
            ConnectionFactory,
            UserAdminRepositoryMock.Object,
            UnitOfWorkMock.Object,
            CurrentUserServiceMock.Object,
            PermissionServiceMock.Object,
            AuditLoggerMock.Object);
    }
}
