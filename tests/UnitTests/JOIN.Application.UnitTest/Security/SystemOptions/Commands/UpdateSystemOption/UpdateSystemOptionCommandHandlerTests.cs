using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings.Security.SystemOption;
using JOIN.Application.UseCases.Security.SystemOptions.Commands;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.Security.SystemOptions.Commands.UpdateSystemOption;

/// <summary>
/// Tests for the SystemOption update command handler.
/// Verifies the SPEC 21 new fields flow through ApplyUpdate and reach UpdateAsync.
/// </summary>
public sealed class UpdateSystemOptionCommandHandlerTests
{
    /// <summary>
    /// Happy path: ApplyUpdate receives the new fields, UpdateAsync persists, DTO is returned.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOptionExists_ShouldApplyUpdateAndReturnDto()
    {
        var id = Guid.NewGuid();
        var existing = new SystemOption { Name = "Old" };

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var repoMock = new Mock<IGenericRepository<SystemOption>>();
        unitOfWorkMock.Setup(x => x.GetRepository<SystemOption>()).Returns(repoMock.Object);
        repoMock.Setup(x => x.GetAsync(id)).ReturnsAsync(existing);
        repoMock.Setup(x => x.UpdateAsync(It.IsAny<SystemOption>())).ReturnsAsync(true);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var mapperMock = new Mock<ISystemOptionMapper>();
        var dto = new SystemOptionDto
        {
            Id = id,
            ModuleId = Guid.NewGuid(),
            ModuleName = "Security",
            Name = "Updated",
            Route = "/updated",
            CanRead = true,
            CanCreate = false,
            CanUpdate = true,
            CanDelete = true,
            CanDownload = true,
            CanExport = true,
            CanExecute = false,
            IsVisibleMenu = false,
            OrderMenu = 42,
            Created = DateTime.UtcNow
        };
        mapperMock.Setup(x => x.ToDto(existing)).Returns(dto);

        var handler = new UpdateSystemOptionCommandHandler(unitOfWorkMock.Object, mapperMock.Object);

        var cmd = new UpdateSystemOptionCommand(
            Id: id,
            Name: "Updated",
            Route: "/updated",
            Icon: null,
            ParentId: null,
            ControllerName: null,
            CanRead: true,
            CanCreate: false,
            CanUpdate: true,
            CanDelete: true,
            CanDownload: true,
            CanExport: true,
            CanExecute: false,
            IsVisibleMenu: false,
            OrderMenu: 42);

        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(dto);
        mapperMock.Verify(x => x.ApplyUpdate(cmd, existing), Times.Once);
        repoMock.Verify(x => x.UpdateAsync(existing), Times.Once);
    }

    /// <summary>
    /// Id not found returns SYSTEM_OPTION_NOT_FOUND and skips persistence.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOptionMissing_ShouldReturnNotFound()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var repoMock = new Mock<IGenericRepository<SystemOption>>();
        unitOfWorkMock.Setup(x => x.GetRepository<SystemOption>()).Returns(repoMock.Object);
        repoMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync((SystemOption?)null);

        var mapperMock = new Mock<ISystemOptionMapper>();
        var handler = new UpdateSystemOptionCommandHandler(unitOfWorkMock.Object, mapperMock.Object);

        var cmd = new UpdateSystemOptionCommand(Guid.NewGuid(), "X", "/x", null, null, null);
        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SYSTEM_OPTION_NOT_FOUND");
        repoMock.Verify(x => x.UpdateAsync(It.IsAny<SystemOption>()), Times.Never);
    }
}