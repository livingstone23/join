using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Regions.Commands;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Regions.Commands.DeleteRegion;

/// <summary>
/// Contains the unit tests for the region delete command.
/// These tests verify dependency protection and the soft-delete behavior.
/// </summary>
public sealed class DeleteRegionCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the region does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRegionDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var regionId = _fixture.Create<Guid>();
        var context = new DeleteRegionCommandTestContext();
        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync((Region?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteRegionCommand(regionId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("REGION_NOT_FOUND");
        response.Errors.Should().Contain("Region not found.");
    }

    /// <summary>
    /// Verifies the protected branch when active provinces reference the region.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRegionIsUsedByProvinces_ShouldReturnInUseError()
    {
        // Arrange
        var regionId = _fixture.Create<Guid>();
        var context = new DeleteRegionCommandTestContext();
        var entity = CreateRegion(regionId);

        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync(entity);
        context.ProvinceRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            new Province { Name = "Managua", Code = "MN", CountryId = Guid.NewGuid(), RegionId = regionId, GcRecord = 0 }
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteRegionCommand(regionId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("REGION_IN_USE");
        response.Errors.Should().Contain("The region is currently linked to active provinces and cannot be deleted.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the soft delete is not committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var regionId = _fixture.Create<Guid>();
        var context = new DeleteRegionCommandTestContext();
        var entity = CreateRegion(regionId);

        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync(entity);
        context.ProvinceRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Province>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteRegionCommand(regionId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the region.");
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
    }

    /// <summary>
    /// Verifies the happy path when the region can be soft deleted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRegionIsNotInUse_ShouldSoftDeleteAndReturnSuccess()
    {
        // Arrange
        var regionId = _fixture.Create<Guid>();
        var context = new DeleteRegionCommandTestContext();
        var entity = CreateRegion(regionId);

        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync(entity);
        context.ProvinceRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Province>());

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteRegionCommand(regionId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Region deleted successfully.");
        response.Data.Should().Be(regionId);
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
        context.RegionRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    /// <summary>
    /// Creates a region entity for delete scenarios.
    /// </summary>
    private static Region CreateRegion(Guid regionId)
    {
        var entity = new Region
        {
            Name = "North",
            Code = "NI",
            CountryId = Guid.NewGuid()
        };

        typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(entity, regionId);
        return entity;
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the delete handler.
    /// </summary>
    private sealed class DeleteRegionCommandTestContext
    {
        public DeleteRegionCommandTestContext()
        {
            RegionRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Region>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<Region>()).Returns(RegionRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Province>()).Returns(ProvinceRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Region>> RegionRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Province>> ProvinceRepositoryMock { get; } = new();

        public DeleteRegionCommandHandler CreateHandler()
        {
            return new DeleteRegionCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
