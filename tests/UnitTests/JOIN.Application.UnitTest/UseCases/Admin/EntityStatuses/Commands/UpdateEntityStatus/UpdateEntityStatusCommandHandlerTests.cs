using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.EntityStatuses.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.EntityStatuses.Commands.UpdateEntityStatus;

/// <summary>
/// Contains the unit tests for the entity status update command.
/// These tests verify company validation, not-found protection,
/// duplicate checks, persistence failures, and the successful update flow.
/// </summary>
public sealed class UpdateEntityStatusCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant company identifier is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new UpdateEntityStatusCommandTestContext();
        var request = CreateValidCommand(_fixture.Create<Guid>(), Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

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
        var request = CreateValidCommand(_fixture.Create<Guid>(), companyId);
        var context = new UpdateEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync((Company?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The specified CompanyId does not exist.");
    }

    /// <summary>
    /// Verifies the not-found branch when the requested entity status does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityStatusDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var request = CreateValidCommand(statusId, companyId);
        var context = new UpdateEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync((EntityStatus?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ENTITY_STATUS_NOT_FOUND");
        response.Errors.Should().Contain("Entity status not found.");
    }

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherStatusUsesSameName_ShouldReturnNameInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entity = CreateExistingEntityStatus(_fixture.Create<Guid>());
        var request = CreateValidCommand(entity.Id, companyId);
        var context = new UpdateEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.StatusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                entity,
                CreateExistingEntityStatus(_fixture.Create<Guid>(), request.Name.Trim().ToUpperInvariant(), 99)
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ENTITY_STATUS_NAME_IN_USE");
        response.Errors.Should().Contain("Another active entity status already uses the same name.");
    }

    /// <summary>
    /// Verifies the duplicate-code branch when another active record already uses the same code.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherStatusUsesSameCode_ShouldReturnCodeInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entity = CreateExistingEntityStatus(_fixture.Create<Guid>());
        var request = CreateValidCommand(entity.Id, companyId);
        var context = new UpdateEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.StatusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                entity,
                CreateExistingEntityStatus(_fixture.Create<Guid>(), "Other", request.Code)
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ENTITY_STATUS_CODE_IN_USE");
        response.Errors.Should().Contain("Another active entity status already uses the same code.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when no rows are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entity = CreateExistingEntityStatus(_fixture.Create<Guid>());
        var request = CreateValidCommand(entity.Id, companyId);
        var context = new UpdateEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.StatusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync([entity]);

        context.StatusRepositoryMock
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
        response.Errors.Should().Contain("No records were affected while updating the entity status.");
    }

    /// <summary>
    /// Verifies the happy path when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateEntityStatusAndReturnDto()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entity = CreateExistingEntityStatus(_fixture.Create<Guid>());
        var request = CreateValidCommand(entity.Id, companyId);
        var context = new UpdateEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.StatusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync([entity]);

        context.StatusRepositoryMock
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
        response.Message.Should().Be("Entity status updated successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(entity.Id);
        response.Data.Name.Should().Be("Active");
        response.Data.Description.Should().Be("Operational state");
        response.Data.Code.Should().Be(10);
        response.Data.IsOperative.Should().BeTrue();
    }

    /// <summary>
    /// Creates a valid command instance for the update flow.
    /// </summary>
    private static UpdateEntityStatusCommand CreateValidCommand(Guid id, Guid companyId)
    {
        return new UpdateEntityStatusCommand
        {
            Id = id,
            CompanyId = companyId,
            Name = "  Active  ",
            Description = "  Operational state  ",
            Code = 10,
            IsOperative = true
        };
    }

    /// <summary>
    /// Creates a reusable existing entity status for update scenarios.
    /// </summary>
    private static EntityStatus CreateExistingEntityStatus(Guid id, string name = "Legacy", int code = 5)
    {
        var entity = new EntityStatus
        {
            Name = name,
            Description = "Old description",
            Code = code,
            IsOperative = false,
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
    private sealed class UpdateEntityStatusCommandTestContext
    {
        public UpdateEntityStatusCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, StatusRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<EntityStatus>> StatusRepositoryMock { get; } = new();

        public UpdateEntityStatusCommandHandler CreateHandler()
        {
            return new UpdateEntityStatusCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
