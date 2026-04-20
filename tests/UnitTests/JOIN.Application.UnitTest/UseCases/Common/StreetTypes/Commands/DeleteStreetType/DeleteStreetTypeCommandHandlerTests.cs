using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.StreetTypes.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.StreetTypes.Commands.DeleteStreetType;

/// <summary>
/// Contains the unit tests for the street type soft-delete command handler.
/// These tests verify not-found protection, persistence failures, and the successful delete flow.
/// </summary>
public sealed class DeleteStreetTypeCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the requested street type does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStreetTypeDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var streetTypeId = _fixture.Create<Guid>();
        var request = new DeleteStreetTypeCommand(streetTypeId);
        var context = new DeleteStreetTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(streetTypeId))
            .ReturnsAsync((StreetType?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("STREETTYPE_NOT_FOUND");
        response.Errors.Should().Contain("Street type not found.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<StreetType>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var entity = new StreetType
        {
            Name = "Avenue",
            Abbreviation = "Ave",
            IsActive = true,
            GcRecord = 0
        };

        var request = new DeleteStreetTypeCommand(entity.Id);
        var context = new DeleteStreetTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the street type.");
    }

    /// <summary>
    /// Verifies the happy path when the street type is successfully soft-deleted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldSoftDeleteStreetTypeAndReturnId()
    {
        // Arrange
        var entity = new StreetType
        {
            Name = "Avenue",
            Abbreviation = "Ave",
            IsActive = true,
            GcRecord = 0
        };

        var request = new DeleteStreetTypeCommand(entity.Id);
        var context = new DeleteStreetTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Street type deleted successfully.");
        response.Data.Should().Be(entity.Id);

        entity.GcRecord.Should().NotBe(0);

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Registers a repository in the mocked unit of work using the generic resolution pattern.
    /// </summary>
    private static void SetupRepository<TEntity>(Mock<IUnitOfWork> unitOfWorkMock, Mock<IGenericRepository<TEntity>> repositoryMock)
        where TEntity : class
    {
        unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the delete handler.
    /// </summary>
    private sealed class DeleteStreetTypeCommandTestContext
    {
        public DeleteStreetTypeCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<StreetType>> RepositoryMock { get; } = new();

        public DeleteStreetTypeCommandHandler CreateHandler()
        {
            return new DeleteStreetTypeCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
