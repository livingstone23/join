using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.SystemModules.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.SystemModules.Commands.DeleteSystemModule;

/// <summary>
/// Contains the unit tests for the logical delete flow of system modules.
/// These tests verify not-found behavior, in-use protection,
/// persistence failures, and the successful soft-delete path.
/// </summary>
public sealed class DeleteSystemModuleCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the requested module does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSystemModuleDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var context = new DeleteSystemModuleCommandTestContext();
        var command = new DeleteSystemModuleCommand(_fixture.Create<Guid>());
        var handler = context.CreateHandler();

        context.RepositoryMock
            .Setup(x => x.GetAsync(command.Id))
            .ReturnsAsync((SystemModule?)null);

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SYSTEM_MODULE_NOT_FOUND");
        response.Errors.Should().Contain("System module not found.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<SystemModule>()), Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when the record was already soft-deleted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSystemModuleIsAlreadyDeleted_ShouldReturnNotFoundError()
    {
        // Arrange
        var entity = CreateExistingModule(_fixture.Create<Guid>(), gcRecord: 20260420);
        var context = new DeleteSystemModuleCommandTestContext();
        var command = new DeleteSystemModuleCommand(entity.Id);
        var handler = context.CreateHandler();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SYSTEM_MODULE_NOT_FOUND");
        response.Errors.Should().Contain("System module not found.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<SystemModule>()), Times.Never);
    }

    /// <summary>
    /// Verifies the in-use protection branch when the module is linked to active system options.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSystemModuleIsInUse_ShouldReturnInUseError()
    {
        // Arrange
        var entity = CreateExistingModule(_fixture.Create<Guid>());
        var context = new DeleteSystemModuleCommandTestContext();
        var command = new DeleteSystemModuleCommand(entity.Id);
        var handler = context.CreateHandler();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.SystemOptionRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new SystemOption
                {
                    ModuleId = entity.Id,
                    Name = "Manage Persons",
                    Route = "/customers",
                    GcRecord = 0
                }
            ]);

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SYSTEM_MODULE_IN_USE");
        response.Errors.Should().Contain("The system module is currently linked to one or more system options and cannot be deleted.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when no rows are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var entity = CreateExistingModule(_fixture.Create<Guid>());
        var context = new DeleteSystemModuleCommandTestContext();
        var command = new DeleteSystemModuleCommand(entity.Id);
        var handler = context.CreateHandler();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.SystemOptionRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<SystemOption>());

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
        response.Errors.Should().Contain("No records were affected while deleting the system module.");
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
    }

    /// <summary>
    /// Verifies the happy path for a logical delete operation.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSystemModuleExists_ShouldSoftDeleteEntityAndReturnSuccess()
    {
        // Arrange
        var entity = CreateExistingModule(_fixture.Create<Guid>());
        var context = new DeleteSystemModuleCommandTestContext();
        var command = new DeleteSystemModuleCommand(entity.Id);
        var handler = context.CreateHandler();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.SystemOptionRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<SystemOption>());

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
        response.Message.Should().Be("System module deleted successfully.");
        response.Data.Should().Be(entity.Id);
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    /// <summary>
    /// Creates a reusable existing module for delete scenarios.
    /// </summary>
    private static SystemModule CreateExistingModule(Guid id, int gcRecord = 0)
    {
        var entity = new SystemModule
        {
            Name = "CRM",
            Description = "Person management",
            Icon = "fa-users",
            IsActive = true,
            GcRecord = gcRecord
        };

        typeof(JOIN.Domain.Audit.BaseEntity)
            .GetProperty("Id")!
            .SetValue(entity, id);

        return entity;
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
    private sealed class DeleteSystemModuleCommandTestContext
    {
        public DeleteSystemModuleCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
            SetupRepository(UnitOfWorkMock, SystemOptionRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<SystemModule>> RepositoryMock { get; } = new();
        public Mock<IGenericRepository<SystemOption>> SystemOptionRepositoryMock { get; } = new();

        public DeleteSystemModuleCommandHandler CreateHandler()
        {
            return new DeleteSystemModuleCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
