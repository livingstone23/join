using FluentAssertions;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Users.Commands.BulkUpdateUserRoles;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Users.Commands.BulkUpdateUserRoles;

/// <summary>
/// SPEC 28 / F6 acceptance criteria for <see cref="BulkUpdateUserRolesCommandHandler"/>:
/// tenant guard, role validity cuts the lot, missing membership does not cut,
/// no-change outcomes, soft-deleted reactivation, and the per-user cache
/// invalidation contract.
/// </summary>
public sealed class BulkUpdateUserRolesCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenTenantIsEmpty_ShouldReturnTenantRequired()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var response = await ctx.Handler.Handle(
            new BulkUpdateUserRolesCommand(new[] { Guid.NewGuid() }, new[] { Guid.NewGuid() }, Array.Empty<Guid>()),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TENANT_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenAnyRoleIdIsFromAnotherTenant_ShouldReturnRoleNotFoundAndWriteNothing()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.UserAdminRepositoryMock
            .Setup(x => x.FilterExistingRoleIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        var response = await ctx.Handler.Handle(
            new BulkUpdateUserRolesCommand(new[] { Guid.NewGuid() }, new[] { Guid.NewGuid() }, Array.Empty<Guid>()),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_NOT_FOUND");
        ctx.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        ctx.PermissionServiceMock.Verify(
            x => x.InvalidateUserCacheAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserHasNoMembership_ShouldReturnUserNotFoundForThatItemAndContinueProcessing()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var userWithMembership = Guid.NewGuid();
        var userWithoutMembership = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.UserAdminRepositoryMock
            .Setup(x => x.FilterExistingRoleIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { roleId });
        ctx.UserAdminRepositoryMock
            .Setup(x => x.FilterUsersWithMembershipAsync(
                It.IsAny<IReadOnlyList<Guid>>(), companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { userWithMembership });

        // No existing assignments — handler inserts fresh row for the member and
        // reports UserNotFound for the non-member without throwing.
        ctx.ConnectionFactory.SetResults(FakeResultSet.FromRows());

        var response = await ctx.Handler.Handle(
            new BulkUpdateUserRolesCommand(
                new[] { userWithMembership, userWithoutMembership },
                new[] { roleId },
                Array.Empty<Guid>()),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().HaveCount(2);
        response.Data.Items.Should().Contain(i =>
            i.UserId == userWithoutMembership && i.Outcome == BulkRoleOutcome.UserNotFound);
        response.Data.Items.Should().Contain(i =>
            i.UserId == userWithMembership && i.Outcome == BulkRoleOutcome.Updated);
        response.Data.UsersSkipped.Should().Be(1);
        response.Data.UsersUpdated.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenUserHasNoMembership_ShouldReturnUserNotFoundWithoutWriting()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var userWithMembership = Guid.NewGuid();
        var userWithoutMembership = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.UserAdminRepositoryMock
            .Setup(x => x.FilterExistingRoleIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { roleId });
        ctx.UserAdminRepositoryMock
            .Setup(x => x.FilterUsersWithMembershipAsync(
                It.IsAny<IReadOnlyList<Guid>>(), companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { userWithMembership });

        // No existing assignments — handler inserts fresh row for the member and
        // reports UserNotFound for the non-member without throwing.
        ctx.ConnectionFactory.SetResults(FakeResultSet.FromRows());

        var response = await ctx.Handler.Handle(
            new BulkUpdateUserRolesCommand(
                new[] { userWithMembership, userWithoutMembership },
                new[] { roleId },
                Array.Empty<Guid>()),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().HaveCount(2);
        response.Data.Items.Should().Contain(i =>
            i.UserId == userWithoutMembership && i.Outcome == BulkRoleOutcome.UserNotFound);
        response.Data.Items.Should().Contain(i =>
            i.UserId == userWithMembership && i.Outcome == BulkRoleOutcome.Updated);
        response.Data.UsersSkipped.Should().Be(1);
        response.Data.UsersUpdated.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenAddingToThreeUsers_ShouldInsertForEachAndInvalidateAllThree()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var userC = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.UserAdminRepositoryMock
            .Setup(x => x.FilterExistingRoleIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { roleId });
        ctx.UserAdminRepositoryMock
            .Setup(x => x.FilterUsersWithMembershipAsync(It.IsAny<IReadOnlyList<Guid>>(), companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { userA, userB, userC });
        ctx.ConnectionFactory.SetResults(FakeResultSet.FromRows());

        var response = await ctx.Handler.Handle(
            new BulkUpdateUserRolesCommand(new[] { userA, userB, userC }, new[] { roleId }, Array.Empty<Guid>()),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.UsersUpdated.Should().Be(3);
        response.Data.Items.Should().OnlyContain(i => i.Outcome == BulkRoleOutcome.Updated);
        ctx.PermissionServiceMock.Verify(
            x => x.InvalidateUserCacheAsync(companyId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        ctx.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenRemovingFromTwoUsers_ShouldSoftDeleteForEach()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var assignmentAId = Guid.NewGuid();
        var assignmentBId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.UserAdminRepositoryMock
            .Setup(x => x.FilterExistingRoleIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { roleId });
        ctx.UserAdminRepositoryMock
            .Setup(x => x.FilterUsersWithMembershipAsync(It.IsAny<IReadOnlyList<Guid>>(), companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { userA, userB });
        ctx.ConnectionFactory.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = assignmentAId,
                ["UserId"] = userA,
                ["RoleId"] = roleId,
                ["GcRecord"] = 0
            },
            new Dictionary<string, object?>
            {
                ["Id"] = assignmentBId,
                ["UserId"] = userB,
                ["RoleId"] = roleId,
                ["GcRecord"] = 0
            }));

        var assignmentA = new UserRoleCompany { GcRecord = 0 };
        var assignmentB = new UserRoleCompany { GcRecord = 0 };
        var repo = new Mock<IGenericRepository<UserRoleCompany>>();
        repo.Setup(x => x.GetAsync(assignmentAId)).ReturnsAsync(assignmentA);
        repo.Setup(x => x.GetAsync(assignmentBId)).ReturnsAsync(assignmentB);
        ctx.UnitOfWorkMock.Setup(x => x.GetRepository<UserRoleCompany>()).Returns(repo.Object);

        var response = await ctx.Handler.Handle(
            new BulkUpdateUserRolesCommand(new[] { userA, userB }, Array.Empty<Guid>(), new[] { roleId }),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.UsersUpdated.Should().Be(2);
        assignmentA.GcRecord.Should().NotBe(0);
        assignmentB.GcRecord.Should().NotBe(0);
        ctx.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAddingToUserWithSoftDeletedRow_ShouldReactivateInPlace()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var softDeletedId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.UserAdminRepositoryMock
            .Setup(x => x.FilterExistingRoleIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { roleId });
        ctx.UserAdminRepositoryMock
            .Setup(x => x.FilterUsersWithMembershipAsync(It.IsAny<IReadOnlyList<Guid>>(), companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { userId });
        ctx.ConnectionFactory.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = softDeletedId,
            ["UserId"] = userId,
            ["RoleId"] = roleId,
            ["GcRecord"] = 20260101
        }));

        var reusable = new UserRoleCompany
        {
            UserId = userId,
            RoleId = roleId,
            CompanyId = companyId,
            GcRecord = 20260101
        };
        var repo = new Mock<IGenericRepository<UserRoleCompany>>();
        repo.Setup(x => x.GetAsync(softDeletedId)).ReturnsAsync(reusable);
        ctx.UnitOfWorkMock.Setup(x => x.GetRepository<UserRoleCompany>()).Returns(repo.Object);

        var response = await ctx.Handler.Handle(
            new BulkUpdateUserRolesCommand(new[] { userId }, new[] { roleId }, Array.Empty<Guid>()),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        reusable.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
        repo.Verify(x => x.InsertAsync(It.IsAny<UserRoleCompany>()), Times.Never);
        repo.Verify(x => x.UpdateAsync(reusable), Times.AtLeastOnce);
    }

    private sealed class Context
    {
        public FakeSqlConnectionFactory ConnectionFactory { get; } = new();
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IUserAdminRepository> UserAdminRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IPermissionService> PermissionServiceMock { get; } = new();

        public Context()
        {
            UnitOfWorkMock
                .Setup(x => x.GetRepository<UserRoleCompany>())
                .Returns(() => Mock.Of<IGenericRepository<UserRoleCompany>>());
        }

        public BulkUpdateUserRolesCommandHandler Handler => new(
            ConnectionFactory,
            UserAdminRepositoryMock.Object,
            UnitOfWorkMock.Object,
            CurrentUserServiceMock.Object,
            PermissionServiceMock.Object,
            NullLogger<BulkUpdateUserRolesCommandHandler>.Instance);
    }
}