using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.Security.RoleSystemOptions.Commands.DeleteRoleSystemOption;

/// <summary>
/// Tests for the role-system-option delete handler (SPEC 23).
/// The tenant is always derived from <see cref="ICurrentUserService"/>; a command-supplied
/// CompanyId that differs from the token is rejected with COMPANY_MISMATCH.
/// </summary>
public sealed class DeleteRoleSystemOptionCommandHandlerTests
{
    /// <summary>
    /// Happy path: handler uses the tenant from the token, soft-deletes the entity, returns the id.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityExists_ShouldSoftDeleteAndReturnId()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var entity = new RoleSystemOption { CompanyId = companyId, GcRecord = 0 };

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var repoMock = new Mock<IRoleSystemOptionsRepository>();
        unitOfWorkMock.Setup(x => x.RoleSystemOptions).Returns(repoMock.Object);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        repoMock
            .Setup(x => x.GetTrackedActiveByIdAndCompanyAsync(id, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        repoMock.Setup(x => x.UpdateAsync(It.IsAny<RoleSystemOption>())).ReturnsAsync(true);

        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(x => x.CompanyId).Returns(companyId);

        var handler = new DeleteRoleSystemOptionCommandHandler(unitOfWorkMock.Object, currentUserServiceMock.Object, Mock.Of<IAuditLogger>());

        var cmd = new DeleteRoleSystemOptionCommand(id);

        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        response.Message.Should().Be("Role system option deleted successfully.");
        repoMock.Verify(x => x.GetTrackedActiveByIdAndCompanyAsync(id, companyId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// When the token has no tenant, the handler short-circuits with INVALID_COMPANY_ID.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTokenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(x => x.CompanyId).Returns(Guid.Empty);

        var handler = new DeleteRoleSystemOptionCommandHandler(unitOfWorkMock.Object, currentUserServiceMock.Object, Mock.Of<IAuditLogger>());

        var cmd = new DeleteRoleSystemOptionCommand(Guid.NewGuid());

        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        unitOfWorkMock.Verify(x => x.RoleSystemOptions, Times.Never);
        unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// When the command sends a CompanyId that differs from the token tenant, the handler rejects with COMPANY_MISMATCH.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCommandCompanyIdMismatchesToken_ShouldReturnCompanyMismatch()
    {
        var tokenCompanyId = Guid.NewGuid();
        var commandCompanyId = Guid.NewGuid();

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(x => x.CompanyId).Returns(tokenCompanyId);

        var handler = new DeleteRoleSystemOptionCommandHandler(unitOfWorkMock.Object, currentUserServiceMock.Object, Mock.Of<IAuditLogger>());

        var cmd = new DeleteRoleSystemOptionCommand(Guid.NewGuid(), CompanyId: commandCompanyId);

        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_MISMATCH");
        unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Entity not found for the caller's tenant returns ROLE_SYSTEM_OPTION_NOT_FOUND.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityMissing_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var repoMock = new Mock<IRoleSystemOptionsRepository>();
        unitOfWorkMock.Setup(x => x.RoleSystemOptions).Returns(repoMock.Object);
        repoMock
            .Setup(x => x.GetTrackedActiveByIdAndCompanyAsync(id, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleSystemOption?)null);

        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(x => x.CompanyId).Returns(companyId);

        var handler = new DeleteRoleSystemOptionCommandHandler(unitOfWorkMock.Object, currentUserServiceMock.Object, Mock.Of<IAuditLogger>());

        var response = await handler.Handle(new DeleteRoleSystemOptionCommand(id), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_SYSTEM_OPTION_NOT_FOUND");
    }
}

