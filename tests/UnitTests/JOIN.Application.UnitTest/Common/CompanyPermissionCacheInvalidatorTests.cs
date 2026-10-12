using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.Common;

/// <summary>
/// Unit tests for <see cref="CompanyPermissionCacheInvalidator"/> (SPEC 43).
/// </summary>
public sealed class CompanyPermissionCacheInvalidatorTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task InvalidateAsync_ShouldDropTheCacheOfActiveMembersAndRoleUsersOfTheCompanyOnce()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var permissionService = new Mock<IPermissionService>();
        var member = Guid.NewGuid();
        var memberWithRole = Guid.NewGuid();
        var roleOnly = Guid.NewGuid();
        var deletedMember = new UserCompany { UserId = Guid.NewGuid(), CompanyId = CompanyId };
        deletedMember.MarkAsDeleted();

        unitOfWork.SetupRepositoryRows(new[]
        {
            new UserCompany { UserId = member, CompanyId = CompanyId },
            new UserCompany { UserId = memberWithRole, CompanyId = CompanyId },
            new UserCompany { UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid() },
            deletedMember
        });
        unitOfWork.SetupRepositoryRows(new[]
        {
            new UserRoleCompany { UserId = memberWithRole, CompanyId = CompanyId },
            new UserRoleCompany { UserId = roleOnly, CompanyId = CompanyId }
        });

        await new CompanyPermissionCacheInvalidator(unitOfWork.Object, permissionService.Object).InvalidateAsync(CompanyId);

        foreach (var userId in new[] { member, memberWithRole, roleOnly })
        {
            permissionService.Verify(x => x.InvalidateUserCacheAsync(CompanyId, userId, It.IsAny<CancellationToken>()), Times.Once);
        }

        permissionService.Verify(x => x.InvalidateUserCacheAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
}
