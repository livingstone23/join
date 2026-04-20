using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.EntityStatuses.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.EntityStatuses.Commands.CreateEntityStatus;

/// <summary>
/// Contains the unit tests for the entity status creation command.
/// These tests verify company validation, duplicate protections,
/// persistence failures, and the successful creation flow.
/// </summary>
public sealed class CreateEntityStatusCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant company identifier is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new CreateEntityStatusCommandTestContext();
        var request = CreateValidCommand(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The X-Company-Id header is required.");
        context.UnitOfWorkMock.Verify(x => x.GetRepository<Company>(), Times.Never);
    }

    /// <summary>
    /// Verifies the validation branch when the requested company does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var request = CreateValidCommand(companyId);
        var context = new CreateEntityStatusCommandTestContext();

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
    /// Verifies the duplicate-name branch using a case-insensitive comparison.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ShouldReturnNameInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var request = CreateValidCommand(companyId);
        var context = new CreateEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new EntityStatus
                {
                    Name = request.Name.Trim().ToUpperInvariant(),
                    Code = 99,
                    GcRecord = 0
                }
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
    public async Task Handle_WhenCodeAlreadyExists_ShouldReturnCodeInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var request = CreateValidCommand(companyId);
        var context = new CreateEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new EntityStatus
                {
                    Name = "Other",
                    Code = request.Code,
                    GcRecord = 0
                }
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
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var request = CreateValidCommand(companyId);
        var context = new CreateEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<EntityStatus>());

        context.StatusRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<EntityStatus>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
        response.Errors.Should().Contain("No records were affected while creating the entity status.");
    }

    /// <summary>
    /// Verifies the happy path when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateEntityStatusAndReturnDto()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var request = CreateValidCommand(companyId);
        var context = new CreateEntityStatusCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.StatusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<EntityStatus>());

        context.StatusRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<EntityStatus>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Entity status created successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Name.Should().Be("Active");
        response.Data.Description.Should().Be("Operational state");
        response.Data.Code.Should().Be(10);
        response.Data.IsOperative.Should().BeTrue();
    }

    /// <summary>
    /// Creates a valid command instance for the creation flow.
    /// </summary>
    private static CreateEntityStatusCommand CreateValidCommand(Guid companyId)
    {
        return new CreateEntityStatusCommand
        {
            CompanyId = companyId,
            Name = "  Active  ",
            Description = "  Operational state  ",
            Code = 10,
            IsOperative = true
        };
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
    /// Holds the reusable mocks and helper factory for the create handler.
    /// </summary>
    private sealed class CreateEntityStatusCommandTestContext
    {
        public CreateEntityStatusCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, StatusRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<EntityStatus>> StatusRepositoryMock { get; } = new();

        public CreateEntityStatusCommandHandler CreateHandler()
        {
            return new CreateEntityStatusCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
