using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security.RoleSystemOption;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.Security.RoleSystemOptions.Commands.UpdateRoleSystemOption;

/// <summary>
/// Tests for the role-system-option update handler.
/// Verifies the SPEC 22 new fields (CanDownload, CanExport, CanExecute, IsVisibleMenu, OrderMenu)
/// are passed into ApplyUpdate, and the SPEC 23 tenant derivation: the tenant is always read
/// from <see cref="ICurrentUserService"/>; a body-supplied CompanyId that differs from the
/// token is rejected with COMPANY_MISMATCH.
/// </summary>
public sealed class UpdateRoleSystemOptionCommandHandlerTests
{
    /// <summary>
    /// Happy path: ApplyUpdate receives the command with the 5 new fields and the response carries the updated DTO.
    /// The handler uses the tenant from the token and ignores request.CompanyId when null.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityExists_ShouldApplyUpdateAndReturnDto()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var entity = new RoleSystemOption
        {
            CompanyId = companyId,
            GcRecord = 0
        };

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var repoMock = new Mock<IRoleSystemOptionsRepository>();
        unitOfWorkMock.Setup(x => x.RoleSystemOptions).Returns(repoMock.Object);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        repoMock
            .Setup(x => x.GetTrackedActiveByIdAndCompanyAsync(id, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        repoMock.Setup(x => x.UpdateAsync(It.IsAny<RoleSystemOption>())).ReturnsAsync(true);
        repoMock.Setup(x => x.GetNamesByIdAndCompanyAsync(It.IsAny<Guid>(), companyId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new RoleSystemOptionNames("Manager", "Manage Tickets", "Acme Corp"));

        var mapperMock = new Mock<IRoleSystemOptionMapper>();
        var dto = new RoleSystemOptionDto
        {
            Id = id,
            CompanyId = companyId,
            CanRead = true,
            CanCreate = false,
            CanUpdate = true,
            CanDelete = false,
            CanDownload = true,
            CanExport = false,
            CanExecute = true,
            IsVisibleMenu = false,
            OrderMenu = 7,
            Created = DateTime.UtcNow
        };
        mapperMock.Setup(x => x.ToDto(It.IsAny<RoleSystemOption>())).Returns(dto);

        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(x => x.CompanyId).Returns(companyId);

        var handler = new UpdateRoleSystemOptionCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, Mock.Of<IAuditLogger>());

        var cmd = new UpdateRoleSystemOptionCommand(
            Id: id,
            CanRead: true,
            CanCreate: false,
            CanUpdate: true,
            CanDelete: false,
            CanDownload: true,
            CanExport: false,
            CanExecute: true,
            IsVisibleMenu: false,
            OrderMenu: 7);

        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(dto.Id);
        response.Data.CompanyId.Should().Be(dto.CompanyId);
        response.Data.OrderMenu.Should().Be(7);
        response.Data.IsVisibleMenu.Should().BeFalse();
        response.Data.CompanyName.Should().Be("Acme Corp");
        response.Data.RoleName.Should().Be("Manager");
        response.Data.SystemOptionName.Should().Be("Manage Tickets");

        mapperMock.Verify(x => x.ApplyUpdate(It.Is<UpdateRoleSystemOptionCommand>(c =>
            c.CanDownload == true
            && c.CanExport == false
            && c.CanExecute == true
            && c.IsVisibleMenu == false
            && c.OrderMenu == 7), entity), Times.Once);
        repoMock.Verify(x => x.GetNamesByIdAndCompanyAsync(It.IsAny<Guid>(), companyId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// When the token has no tenant, the handler short-circuits with INVALID_COMPANY_ID.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTokenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<IRoleSystemOptionMapper>();
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(x => x.CompanyId).Returns(Guid.Empty);

        var handler = new UpdateRoleSystemOptionCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, Mock.Of<IAuditLogger>());

        var cmd = new UpdateRoleSystemOptionCommand(
            Id: Guid.NewGuid(),
            CanRead: true,
            CanCreate: true,
            CanUpdate: true,
            CanDelete: true);

        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// When the body sends a CompanyId that differs from the token tenant, the handler rejects with COMPANY_MISMATCH.
    /// </summary>
    [Fact]
    public async Task Handle_WhenBodyCompanyIdMismatchesToken_ShouldReturnCompanyMismatch()
    {
        var tokenCompanyId = Guid.NewGuid();
        var bodyCompanyId = Guid.NewGuid();

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<IRoleSystemOptionMapper>();
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(x => x.CompanyId).Returns(tokenCompanyId);

        var handler = new UpdateRoleSystemOptionCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, Mock.Of<IAuditLogger>());

        var cmd = new UpdateRoleSystemOptionCommand(
            Id: Guid.NewGuid(),
            CanRead: true,
            CanCreate: true,
            CanUpdate: true,
            CanDelete: true,
            CompanyId: bodyCompanyId);

        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_MISMATCH");
        unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Body CompanyId equal to the token tenant is accepted (backward compatibility with clients that send it).
    /// </summary>
    [Fact]
    public async Task Handle_WhenBodyCompanyIdMatchesToken_ShouldAccept()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var entity = new RoleSystemOption { CompanyId = companyId, GcRecord = 0 };

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var repoMock = new Mock<IRoleSystemOptionsRepository>();
        unitOfWorkMock.Setup(x => x.RoleSystemOptions).Returns(repoMock.Object);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        repoMock.Setup(x => x.GetTrackedActiveByIdAndCompanyAsync(id, companyId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        repoMock.Setup(x => x.UpdateAsync(It.IsAny<RoleSystemOption>())).ReturnsAsync(true);
        repoMock.Setup(x => x.GetNamesByIdAndCompanyAsync(id, companyId, It.IsAny<CancellationToken>())).ReturnsAsync((RoleSystemOptionNames?)null);

        var mapperMock = new Mock<IRoleSystemOptionMapper>();
        mapperMock.Setup(x => x.ToDto(It.IsAny<RoleSystemOption>())).Returns(new RoleSystemOptionDto { Id = id, CompanyId = companyId });

        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(x => x.CompanyId).Returns(companyId);

        var handler = new UpdateRoleSystemOptionCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, Mock.Of<IAuditLogger>());

        var cmd = new UpdateRoleSystemOptionCommand(
            Id: id,
            CanRead: true,
            CanCreate: true,
            CanUpdate: true,
            CanDelete: true,
            CompanyId: companyId);

        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Role system option updated successfully.");
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

        var mapperMock = new Mock<IRoleSystemOptionMapper>();
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(x => x.CompanyId).Returns(companyId);

        var handler = new UpdateRoleSystemOptionCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, Mock.Of<IAuditLogger>());

        var cmd = new UpdateRoleSystemOptionCommand(id, true, true, true, true);

        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_SYSTEM_OPTION_NOT_FOUND");
    }
}

