using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.EntityStatuses.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.EntityStatuses.Commands.DeleteEntityStatus;

/// <summary>
/// Contains the unit tests for the logical delete flow of entity statuses.
/// These tests verify tenant validation, in-use protection,
/// persistence failures, and the successful soft-delete path.
/// </summary>
public sealed class DeleteEntityStatusCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant company identifier is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new DeleteEntityStatusCommandTestContext();
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteEntityStatusCommand(_fixture.Create<Guid>(), Guid.Empty), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The X-Company-Id header is required.");
    }

    /// <summary>
    /// Verifies the validation branch when the requested company does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entityId = _fixture.Create<Guid>();
        var context = new DeleteEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync((Company?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteEntityStatusCommand(entityId, companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The specified CompanyId does not exist.");
    }

    /// <summary>
    /// Verifies the not-found branch when the entity status does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityStatusDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entityId = _fixture.Create<Guid>();
        var context = new DeleteEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(entityId))
            .ReturnsAsync((EntityStatus?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteEntityStatusCommand(entityId, companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ENTITY_STATUS_NOT_FOUND");
        response.Errors.Should().Contain("Entity status not found.");
    }

    /// <summary>
    /// Verifies the in-use protection branch when the entity status is linked to an area.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityStatusIsUsedByAreas_ShouldReturnInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entity = CreateExistingEntityStatus(_fixture.Create<Guid>());
        var context = new DeleteEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.AreaRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new Area
                {
                    CompanyId = companyId,
                    Name = "Support",
                    EntityStatusId = entity.Id,
                    GcRecord = 0
                }
            ]);

        context.ProjectRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Project>());

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteEntityStatusCommand(entity.Id, companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ENTITY_STATUS_IN_USE");
        response.Errors.Should().Contain("The entity status is currently assigned to areas or projects and cannot be deleted.");
    }

    /// <summary>
    /// Verifies the in-use protection branch when the entity status is linked to a project.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityStatusIsUsedByProjects_ShouldReturnInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entity = CreateExistingEntityStatus(_fixture.Create<Guid>());
        var context = new DeleteEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.AreaRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Area>());

        context.ProjectRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new Project
                {
                    CompanyId = companyId,
                    Name = "Implementation",
                    EntityStatusId = entity.Id,
                    GcRecord = 0
                }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteEntityStatusCommand(entity.Id, companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ENTITY_STATUS_IN_USE");
        response.Errors.Should().Contain("The entity status is currently assigned to areas or projects and cannot be deleted.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when no rows are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entity = CreateExistingEntityStatus(_fixture.Create<Guid>());
        var context = new DeleteEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.AreaRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Area>());

        context.ProjectRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Project>());

        context.StatusRepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteEntityStatusCommand(entity.Id, companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the entity status.");
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
    }

    /// <summary>
    /// Verifies the happy path for a logical delete operation.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityStatusExists_ShouldSoftDeleteEntityAndReturnSuccess()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entity = CreateExistingEntityStatus(_fixture.Create<Guid>());
        var context = new DeleteEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.AreaRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Area>());

        context.ProjectRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Project>());

        context.StatusRepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteEntityStatusCommand(entity.Id, companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Entity status deleted successfully.");
        response.Data.Should().Be(entity.Id);
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);

        context.StatusRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    /// <summary>
    /// Creates a reusable existing entity status for delete scenarios.
    /// </summary>
    private static EntityStatus CreateExistingEntityStatus(Guid id)
    {
        var entity = new EntityStatus
        {
            Name = "Active",
            Description = "Operational state",
            Code = 10,
            IsOperative = true,
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
    /// Holds the reusable mocks and helper factory for the delete handler.
    /// </summary>
    private sealed class DeleteEntityStatusCommandTestContext
    {
        public DeleteEntityStatusCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, StatusRepositoryMock);
            SetupRepository(UnitOfWorkMock, AreaRepositoryMock);
            SetupRepository(UnitOfWorkMock, ProjectRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<EntityStatus>> StatusRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Area>> AreaRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Project>> ProjectRepositoryMock { get; } = new();

        public DeleteEntityStatusCommandHandler CreateHandler()
        {
            return new DeleteEntityStatusCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
