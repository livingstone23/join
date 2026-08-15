using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security.RoleSystemOption;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.Security.RoleSystemOptions.Commands.CreateRoleSystemOption;

/// <summary>
/// Tests for the role-system-option creation handler.
/// Verifies the SPEC 22 new fields (CanDownload, CanExport, CanExecute, IsVisibleMenu, OrderMenu)
/// flow from the command through the mapper into the persisted entity and the returned DTO.
/// </summary>
public sealed class CreateRoleSystemOptionCommandHandlerTests
{
    /// <summary>
    /// Happy path: SaveChangesAsync &gt; 0 returns the mapper-produced DTO with all flags populated
    /// and the mapper is invoked with the command carrying the 5 new fields.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesSucceeds_ShouldReturnDtoFromMapper()
    {
        var companyId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var systemOptionId = Guid.NewGuid();

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var companyRepoMock = new Mock<IGenericRepository<Company>>();
        var roleRepoMock = new Mock<IGenericRepository<ApplicationRole>>();
        var optionRepoMock = new Mock<IGenericRepository<SystemOption>>();
        var roleOptionRepoMock = new Mock<IRoleSystemOptionsRepository>();

        unitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(companyRepoMock.Object);
        unitOfWorkMock.Setup(x => x.GetRepository<ApplicationRole>()).Returns(roleRepoMock.Object);
        unitOfWorkMock.Setup(x => x.GetRepository<SystemOption>()).Returns(optionRepoMock.Object);
        unitOfWorkMock.Setup(x => x.RoleSystemOptions).Returns(roleOptionRepoMock.Object);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        companyRepoMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { GcRecord = 0 });
        roleRepoMock.Setup(x => x.GetAsync(roleId)).ReturnsAsync(new ApplicationRole { GcRecord = 0 });
        optionRepoMock.Setup(x => x.GetAsync(systemOptionId)).ReturnsAsync(new SystemOption { GcRecord = 0 });
        roleOptionRepoMock.Setup(x => x.ExistsByRoleAndOptionAsync(companyId, roleId, systemOptionId)).ReturnsAsync(false);

        RoleSystemOption? captured = null;
        roleOptionRepoMock
            .Setup(x => x.InsertAsync(It.IsAny<RoleSystemOption>()))
            .Callback<RoleSystemOption>(e => captured = e)
            .ReturnsAsync(true);

        roleOptionRepoMock.Setup(x => x.GetNamesByIdAndCompanyAsync(It.IsAny<Guid>(), companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleSystemOptionNames("Manager", "Manage Tickets", "Acme Corp"));

        var mapperMock = new Mock<IRoleSystemOptionMapper>();
        var dto = new RoleSystemOptionDto
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            RoleId = roleId,
            SystemOptionId = systemOptionId,
            CanRead = true,
            CanCreate = true,
            CanUpdate = true,
            CanDelete = true,
            CanDownload = true,
            CanExport = false,
            CanExecute = true,
            IsVisibleMenu = true,
            OrderMenu = 5,
            Created = DateTime.UtcNow
        };
        mapperMock.Setup(x => x.ToEntity(It.IsAny<CreateRoleSystemOptionCommand>())).Returns(new RoleSystemOption());
        mapperMock.Setup(x => x.ToDto(It.IsAny<RoleSystemOption>())).Returns(dto);

        var handler = new CreateRoleSystemOptionCommandHandler(unitOfWorkMock.Object, mapperMock.Object);

        var cmd = new CreateRoleSystemOptionCommand(
            CompanyId: companyId,
            RoleId: roleId,
            SystemOptionId: systemOptionId,
            CanRead: true,
            CanCreate: true,
            CanUpdate: true,
            CanDelete: true,
            CanDownload: true,
            CanExport: false,
            CanExecute: true,
            IsVisibleMenu: true,
            OrderMenu: 5);

        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(dto.Id);
        response.Data.CompanyId.Should().Be(dto.CompanyId);
        response.Data.RoleId.Should().Be(dto.RoleId);
        response.Data.SystemOptionId.Should().Be(dto.SystemOptionId);
        response.Data.CompanyName.Should().Be("Acme Corp");
        response.Data.RoleName.Should().Be("Manager");
        response.Data.SystemOptionName.Should().Be("Manage Tickets");
        response.Data.CanDownload.Should().BeTrue();
        response.Data.CanExport.Should().BeFalse();
        response.Data.CanExecute.Should().BeTrue();
        response.Data.IsVisibleMenu.Should().BeTrue();
        response.Data.OrderMenu.Should().Be(5);

        mapperMock.Verify(x => x.ToDto(It.IsAny<RoleSystemOption>()), Times.Once);

        mapperMock.Verify(x => x.ToEntity(It.Is<CreateRoleSystemOptionCommand>(c =>
            c.CanDownload == true
            && c.CanExport == false
            && c.CanExecute == true
            && c.IsVisibleMenu == true
            && c.OrderMenu == 5)), Times.Once);
    }

    /// <summary>
    /// CompanyId == Guid.Empty short-circuits with INVALID_COMPANY_ID before any repository call.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<IRoleSystemOptionMapper>();

        var handler = new CreateRoleSystemOptionCommandHandler(unitOfWorkMock.Object, mapperMock.Object);

        var cmd = new CreateRoleSystemOptionCommand(
            CompanyId: Guid.Empty,
            RoleId: Guid.NewGuid(),
            SystemOptionId: Guid.NewGuid(),
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
    /// SaveChangesAsync returning 0 produces an error response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        var companyId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var systemOptionId = Guid.NewGuid();

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var companyRepoMock = new Mock<IGenericRepository<Company>>();
        var roleRepoMock = new Mock<IGenericRepository<ApplicationRole>>();
        var optionRepoMock = new Mock<IGenericRepository<SystemOption>>();
        var roleOptionRepoMock = new Mock<IRoleSystemOptionsRepository>();

        unitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(companyRepoMock.Object);
        unitOfWorkMock.Setup(x => x.GetRepository<ApplicationRole>()).Returns(roleRepoMock.Object);
        unitOfWorkMock.Setup(x => x.GetRepository<SystemOption>()).Returns(optionRepoMock.Object);
        unitOfWorkMock.Setup(x => x.RoleSystemOptions).Returns(roleOptionRepoMock.Object);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        companyRepoMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { GcRecord = 0 });
        roleRepoMock.Setup(x => x.GetAsync(roleId)).ReturnsAsync(new ApplicationRole { GcRecord = 0 });
        optionRepoMock.Setup(x => x.GetAsync(systemOptionId)).ReturnsAsync(new SystemOption { GcRecord = 0 });
        roleOptionRepoMock.Setup(x => x.ExistsByRoleAndOptionAsync(companyId, roleId, systemOptionId)).ReturnsAsync(false);
        roleOptionRepoMock.Setup(x => x.InsertAsync(It.IsAny<RoleSystemOption>())).ReturnsAsync(true);

        var mapperMock = new Mock<IRoleSystemOptionMapper>();
        mapperMock.Setup(x => x.ToEntity(It.IsAny<CreateRoleSystemOptionCommand>())).Returns(new RoleSystemOption());

        var handler = new CreateRoleSystemOptionCommandHandler(unitOfWorkMock.Object, mapperMock.Object);

        var cmd = new CreateRoleSystemOptionCommand(companyId, roleId, systemOptionId, true, true, true, true);
        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
    }

    /// <summary>
    /// When the post-insert readback returns null (the projection query could not match the row),
    /// the handler falls back to mapper.ToDto(entity) so the caller still receives the persisted id + flags.
    /// </summary>
    [Fact]
    public async Task Handle_WhenGetWithNamesReturnsNull_ShouldFallbackToEntityDto()
    {
        var companyId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var systemOptionId = Guid.NewGuid();

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var companyRepoMock = new Mock<IGenericRepository<Company>>();
        var roleRepoMock = new Mock<IGenericRepository<ApplicationRole>>();
        var optionRepoMock = new Mock<IGenericRepository<SystemOption>>();
        var roleOptionRepoMock = new Mock<IRoleSystemOptionsRepository>();

        unitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(companyRepoMock.Object);
        unitOfWorkMock.Setup(x => x.GetRepository<ApplicationRole>()).Returns(roleRepoMock.Object);
        unitOfWorkMock.Setup(x => x.GetRepository<SystemOption>()).Returns(optionRepoMock.Object);
        unitOfWorkMock.Setup(x => x.RoleSystemOptions).Returns(roleOptionRepoMock.Object);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        companyRepoMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { GcRecord = 0 });
        roleRepoMock.Setup(x => x.GetAsync(roleId)).ReturnsAsync(new ApplicationRole { GcRecord = 0 });
        optionRepoMock.Setup(x => x.GetAsync(systemOptionId)).ReturnsAsync(new SystemOption { GcRecord = 0 });
        roleOptionRepoMock.Setup(x => x.ExistsByRoleAndOptionAsync(companyId, roleId, systemOptionId)).ReturnsAsync(false);
        roleOptionRepoMock.Setup(x => x.InsertAsync(It.IsAny<RoleSystemOption>())).ReturnsAsync(true);
        roleOptionRepoMock.Setup(x => x.GetNamesByIdAndCompanyAsync(It.IsAny<Guid>(), companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleSystemOptionNames?)null);

        var mapperMock = new Mock<IRoleSystemOptionMapper>();
        mapperMock.Setup(x => x.ToEntity(It.IsAny<CreateRoleSystemOptionCommand>())).Returns(new RoleSystemOption());
        var fallbackDto = new RoleSystemOptionDto { Id = Guid.NewGuid(), CompanyId = companyId };
        mapperMock.Setup(x => x.ToDto(It.IsAny<RoleSystemOption>())).Returns(fallbackDto);

        var handler = new CreateRoleSystemOptionCommandHandler(unitOfWorkMock.Object, mapperMock.Object);

        var cmd = new CreateRoleSystemOptionCommand(companyId, roleId, systemOptionId, true, true, true, true);
        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(fallbackDto);
        mapperMock.Verify(x => x.ToDto(It.IsAny<RoleSystemOption>()), Times.Once);
    }
}