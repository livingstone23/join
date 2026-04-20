using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.IdentificationTypes.Commands.DeleteIdentificationType;

/// <summary>
/// Contains the unit tests for the logical delete flow of identification types.
/// These tests verify not-found behavior, persistence failures,
/// and the successful soft-delete path.
/// </summary>
public sealed class DeleteIdentificationTypeCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the requested identification type does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIdentificationTypeDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var context = new DeleteIdentificationTypeCommandTestContext();
        var command = new DeleteIdentificationTypeCommand(_fixture.Create<Guid>());
        var handler = context.CreateHandler();

        context.RepositoryMock
            .Setup(x => x.GetAsync(command.Id))
            .ReturnsAsync((IdentificationType?)null);

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("IDENTIFICATION_TYPE_NOT_FOUND");
        response.Errors.Should().Contain("Identification type not found.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<IdentificationType>()), Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when the record was already soft-deleted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIdentificationTypeIsAlreadyDeleted_ShouldReturnNotFoundError()
    {
        // Arrange
        var entity = new IdentificationType
        {
            Name = "Passport",
            IsActive = false,
            GcRecord = 20260420
        };

        var context = new DeleteIdentificationTypeCommandTestContext();
        var command = new DeleteIdentificationTypeCommand(entity.Id);
        var handler = context.CreateHandler();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("IDENTIFICATION_TYPE_NOT_FOUND");
        response.Errors.Should().Contain("Identification type not found.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<IdentificationType>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var entity = new IdentificationType
        {
            Name = "Passport",
            IsActive = true,
            GcRecord = 0
        };

        var context = new DeleteIdentificationTypeCommandTestContext();
        var command = new DeleteIdentificationTypeCommand(entity.Id);
        var handler = context.CreateHandler();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the identification type.");
    }

    /// <summary>
    /// Verifies the happy path for a logical delete operation.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIdentificationTypeExists_ShouldSoftDeleteEntityAndReturnSuccess()
    {
        // Arrange
        var entity = new IdentificationType
        {
            Name = "Passport",
            IsActive = true,
            GcRecord = 0
        };

        var context = new DeleteIdentificationTypeCommandTestContext();
        var command = new DeleteIdentificationTypeCommand(entity.Id);
        var handler = context.CreateHandler();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Identification type deleted successfully.");
        response.Data.Should().Be(entity.Id);
        entity.IsActive.Should().BeFalse();
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);

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
    private sealed class DeleteIdentificationTypeCommandTestContext
    {
        public DeleteIdentificationTypeCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<IdentificationType>> RepositoryMock { get; } = new();

        public DeleteIdentificationTypeCommandHandler CreateHandler()
        {
            return new DeleteIdentificationTypeCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
