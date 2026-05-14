using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.SystemModules.Commands;
using JOIN.Domain.Admin;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.SystemModules.Commands.UpdateSystemModule;

/// <summary>
/// Contains the unit tests for the system module update command.
/// These tests verify not-found protection, duplicate-name validation,
/// persistence failures, and the successful update flow.
/// </summary>
public sealed class UpdateSystemModuleCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the requested system module does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSystemModuleDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var request = CreateValidCommand(_fixture.Create<Guid>());
        var context = new UpdateSystemModuleCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync((SystemModule?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

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
    public async Task Handle_WhenSystemModuleIsSoftDeleted_ShouldReturnNotFoundError()
    {
        // Arrange
        var request = CreateValidCommand(_fixture.Create<Guid>());
        var context = new UpdateSystemModuleCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(new SystemModule
            {
                Name = "Legacy",
                GcRecord = 20260420
            });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SYSTEM_MODULE_NOT_FOUND");
        response.Errors.Should().Contain("System module not found.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<SystemModule>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherSystemModuleUsesSameName_ShouldReturnNameInUseError()
    {
        // Arrange
        var entity = CreateExistingModule(_fixture.Create<Guid>());
        var request = CreateValidCommand(entity.Id);
        var context = new UpdateSystemModuleCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                entity,
                CreateExistingModule(_fixture.Create<Guid>(), request.Name.Trim().ToUpperInvariant())
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SYSTEM_MODULE_NAME_IN_USE");
        response.Errors.Should().Contain("Another active system module already uses the same name.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<SystemModule>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no rows are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var entity = CreateExistingModule(_fixture.Create<Guid>());
        var request = CreateValidCommand(entity.Id);
        var context = new UpdateSystemModuleCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync([entity]);

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
        response.Message.Should().Be("UPDATE_FAILED");
        response.Errors.Should().Contain("No records were affected while updating the system module.");
    }

    /// <summary>
    /// Verifies the happy path when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateSystemModuleAndReturnDto()
    {
        // Arrange
        var entity = CreateExistingModule(_fixture.Create<Guid>());
        var request = CreateValidCommand(entity.Id);
        var context = new UpdateSystemModuleCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync([entity]);

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
        response.Message.Should().Be("System module updated successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(entity.Id);
        response.Data.Name.Should().Be("CRM");
        response.Data.Description.Should().Be("Person management");
        response.Data.Icon.Should().Be("fa-users");
        response.Data.IsActive.Should().BeTrue();
        response.Data.Order.Should().Be(5);

        entity.Name.Should().Be("CRM");
        entity.Description.Should().Be("Person management");
        entity.Icon.Should().Be("fa-users");
        entity.Order.Should().Be(5);

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    /// <summary>
    /// Creates a valid command instance for the update flow.
    /// </summary>
    private static UpdateSystemModuleCommand CreateValidCommand(Guid id)
    {
        return new UpdateSystemModuleCommand
        {
            Id = id,
            Name = "  CRM  ",
            Description = "  Person management  ",
            Icon = "  fa-users  ",
            IsActive = true,
            Order = 5
        };
    }

    /// <summary>
    /// Creates a reusable existing module for update scenarios.
    /// </summary>
    private static SystemModule CreateExistingModule(Guid id, string name = "Legacy")
    {
        var entity = new SystemModule
        {
            Name = name,
            Description = "Old description",
            Icon = "old-icon",
            IsActive = false,
            Order = null,
            GcRecord = 0
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
    /// Holds the reusable mocks and helper factory for the update handler.
    /// </summary>
    private sealed class UpdateSystemModuleCommandTestContext
    {
        public UpdateSystemModuleCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<SystemModule>> RepositoryMock { get; } = new();

        public UpdateSystemModuleCommandHandler CreateHandler()
        {
            return new UpdateSystemModuleCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
