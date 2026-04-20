using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Areas.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Areas.Commands.DeleteArea;

/// <summary>
/// Contains the unit tests for the logical delete flow of areas.
/// These tests verify tenant protection, not-found behavior, persistence failures,
/// and the successful soft-delete path.
/// </summary>
public sealed class DeleteAreaCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant company identifier is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new DeleteAreaCommandTestContext();
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteAreaCommand(_fixture.Create<Guid>(), Guid.Empty), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The X-Company-Id header is required.");

        context.UnitOfWorkMock.Verify(x => x.GetRepository<Area>(), Times.Never);
        context.AreaRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Area>()), Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when the area does not exist for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAreaDoesNotExist_ShouldReturnAreaNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var areaId = _fixture.Create<Guid>();
        var context = new DeleteAreaCommandTestContext();

        context.AreaRepositoryMock
            .Setup(x => x.GetAsync(areaId))
            .ReturnsAsync((Area?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteAreaCommand(areaId, companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("AREA_NOT_FOUND");
        response.Errors.Should().Contain("Area not found.");
        context.AreaRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Area>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var area = new Area
        {
            CompanyId = companyId,
            Name = "Support",
            EntityStatusId = _fixture.Create<Guid>(),
            GcRecord = 0
        };

        var context = new DeleteAreaCommandTestContext();
        context.AreaRepositoryMock
            .Setup(x => x.GetAsync(area.Id))
            .ReturnsAsync(area);

        context.AreaRepositoryMock
            .Setup(x => x.UpdateAsync(area))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteAreaCommand(area.Id, companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the area.");
    }

    /// <summary>
    /// Verifies the happy path for a logical delete operation.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAreaExists_ShouldMarkEntityAsDeletedAndReturnSuccess()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var area = new Area
        {
            CompanyId = companyId,
            Name = "Support",
            EntityStatusId = _fixture.Create<Guid>(),
            GcRecord = 0
        };

        var context = new DeleteAreaCommandTestContext();
        context.AreaRepositoryMock
            .Setup(x => x.GetAsync(area.Id))
            .ReturnsAsync(area);

        context.AreaRepositoryMock
            .Setup(x => x.UpdateAsync(area))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteAreaCommand(area.Id, companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Area deleted successfully.");
        response.Data.Should().Be(area.Id);
        area.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);

        context.AreaRepositoryMock.Verify(x => x.UpdateAsync(area), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Registers a repository in the mocked unit of work using the generic resolution pattern.
    /// </summary>
    private static void SetupRepository<TEntity>(
        Mock<IUnitOfWork> unitOfWorkMock,
        Mock<IGenericRepository<TEntity>> repositoryMock)
        where TEntity : class
    {
        unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the delete handler.
    /// </summary>
    private sealed class DeleteAreaCommandTestContext
    {
        public DeleteAreaCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, AreaRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Area>> AreaRepositoryMock { get; } = new();

        public DeleteAreaCommandHandler CreateHandler()
        {
            return new DeleteAreaCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
