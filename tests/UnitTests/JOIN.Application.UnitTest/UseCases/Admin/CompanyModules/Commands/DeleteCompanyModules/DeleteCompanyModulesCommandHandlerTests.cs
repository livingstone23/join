using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Admin.CompanyModules.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.CompanyModules.Commands.DeleteCompanyModules;

/// <summary>
/// Unit tests for <see cref="DeleteCompanyModulesCommandHandler"/>: logical delete of the assignment only
/// (SPEC 43, A9) and cache invalidation of the company's users.
/// </summary>
public sealed class DeleteCompanyModulesCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenAssignmentDoesNotExist_ShouldReturnNotFound()
    {
        var context = new TestContext();

        var response = await context.CreateHandler().Handle(new DeleteCompanyModulesCommand(Guid.NewGuid(), CompanyId), CancellationToken.None);

        response.Message.Should().Be("COMPANY_MODULE_NOT_FOUND");
        context.PermissionServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenNothingIsSaved_ShouldReturnDeleteFailedWithoutInvalidating()
    {
        var context = new TestContext(affectedRows: 0);
        var entity = context.SetupAssignment();

        var response = await context.CreateHandler().Handle(new DeleteCompanyModulesCommand(entity.Id, CompanyId), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
        context.PermissionServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenDeleted_ShouldSoftDeleteOnlyTheAssignmentAndInvalidateTheCache()
    {
        var context = new TestContext();
        var entity = context.SetupAssignment();
        var userId = Guid.NewGuid();
        context.UnitOfWorkMock.SetupRepositoryRows(new[] { new UserRoleCompany { UserId = userId, CompanyId = CompanyId } });

        var response = await context.CreateHandler().Handle(new DeleteCompanyModulesCommand(entity.Id, CompanyId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.IsDeleted.Should().BeTrue();
        context.CompanyModuleRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.PermissionServiceMock.Verify(x => x.InvalidateUserCacheAsync(CompanyId, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class TestContext
    {
        public TestContext(int affectedRows = 1)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole(It.IsAny<string>())).Returns(false);
            UnitOfWorkMock.Setup(x => x.CompanyModules).Returns(CompanyModuleNamedRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CompanyModule>()).Returns(CompanyModuleRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(affectedRows);
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<UserCompany>());
            UnitOfWorkMock.SetupRepositoryRows(Array.Empty<UserRoleCompany>());
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICompanyModuleRepository> CompanyModuleNamedRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CompanyModule>> CompanyModuleRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IPermissionService> PermissionServiceMock { get; } = new();

        public CompanyModule SetupAssignment()
        {
            var entity = new CompanyModule { CompanyId = CompanyId, ModuleId = Guid.NewGuid(), IsActive = true };
            CompanyModuleNamedRepositoryMock
                .Setup(x => x.GetByIdForUpdateAsync(entity.Id, CompanyId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(entity);
            return entity;
        }

        public DeleteCompanyModulesCommandHandler CreateHandler() =>
            new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object, new CompanyPermissionCacheInvalidator(UnitOfWorkMock.Object, PermissionServiceMock.Object), Mock.Of<IPermissionService>());
    }
}
