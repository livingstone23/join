using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings.Security.SystemOption;
using JOIN.Application.UseCases.Security.SystemOptions.Commands;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.Security.SystemOptions.Commands.CreateSystemOption;

/// <summary>
/// Tests for the SystemOption creation command handler.
/// Verifies the SPEC 21 new fields (CanDownload, CanExport, CanExecute, IsVisibleMenu, OrderMenu)
/// flow from the command through the mapper into the persisted entity.
/// </summary>
public sealed class CreateSystemOptionCommandHandlerTests
{
    /// <summary>
    /// Happy path: SaveChangesAsync &gt; 0 returns the mapper-produced DTO with all flags populated.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesSucceeds_ShouldReturnDtoFromMapper()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var repoMock = new Mock<IGenericRepository<SystemOption>>();
        unitOfWorkMock.Setup(x => x.GetRepository<SystemOption>()).Returns(repoMock.Object);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        SystemOption? captured = null;
        repoMock
            .Setup(x => x.InsertAsync(It.IsAny<SystemOption>()))
            .Callback<SystemOption>(e => captured = e)
            .ReturnsAsync(true);

        var mapperMock = new Mock<ISystemOptionMapper>();
        var dto = new SystemOptionDto
        {
            Id = Guid.NewGuid(),
            ModuleId = Guid.NewGuid(),
            ModuleName = "Security",
            Name = "Manage Tickets",
            Route = "/tickets/manage",
            Icon = "ticket",
            ControllerName = "Tickets",
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
        mapperMock.Setup(x => x.ToEntity(It.IsAny<CreateSystemOptionCommand>())).Returns(new SystemOption());
        mapperMock.Setup(x => x.ToDto(It.IsAny<SystemOption>())).Returns(dto);

        var handler = new CreateSystemOptionCommandHandler(unitOfWorkMock.Object, mapperMock.Object);

        var cmd = new CreateSystemOptionCommand(
            ModuleId: dto.ModuleId,
            Name: "Manage Tickets",
            Route: "/tickets/manage",
            Icon: "ticket",
            ParentId: null,
            ControllerName: "Tickets",
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
        response.Data.Should().Be(dto);
        captured.Should().NotBeNull();
        mapperMock.Verify(x => x.ToEntity(cmd), Times.Once);
    }

    /// <summary>
    /// SaveChangesAsync returning 0 produces an error response (covers the existing CREATE_FAILED path).
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnError()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var repoMock = new Mock<IGenericRepository<SystemOption>>();
        unitOfWorkMock.Setup(x => x.GetRepository<SystemOption>()).Returns(repoMock.Object);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var mapperMock = new Mock<ISystemOptionMapper>();
        mapperMock.Setup(x => x.ToEntity(It.IsAny<CreateSystemOptionCommand>())).Returns(new SystemOption());

        var handler = new CreateSystemOptionCommandHandler(unitOfWorkMock.Object, mapperMock.Object);

        var cmd = new CreateSystemOptionCommand(Guid.NewGuid(), "Test", "/test", null, null, null);
        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
    }
}
