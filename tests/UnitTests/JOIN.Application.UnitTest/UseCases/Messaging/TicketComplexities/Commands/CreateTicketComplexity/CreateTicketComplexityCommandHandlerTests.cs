using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketComplexities.Commands;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketComplexities.Commands.CreateTicketComplexity;

/// <summary>
/// Contains the unit tests for the ticket complexity create command.
/// These tests verify tenant protection, duplicate validation, related time-unit validation, and successful persistence.
/// </summary>
public sealed class CreateTicketComplexityCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the CompanyId claim is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new CreateTicketComplexityCommandTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
    }

    /// <summary>
    /// Verifies the duplicate-name branch using trimmed and case-insensitive matching.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameIsAlreadyUsed_ShouldReturnNameInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketComplexityCommandTestContext(companyId);
        context.TicketComplexityRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new TicketComplexity { Name = "Critical", Code = 9, GcRecord = 0 }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(name: "  critical  "), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_COMPLEXITY_NAME_IN_USE");
    }

    /// <summary>
    /// Verifies the duplicate-code branch when another active item already uses the requested code.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeIsAlreadyUsed_ShouldReturnCodeInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketComplexityCommandTestContext(companyId);
        context.TicketComplexityRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new TicketComplexity { Name = "Low", Code = 50, GcRecord = 0 }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(code: 50), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_COMPLEXITY_CODE_IN_USE");
    }

    /// <summary>
    /// Verifies the related time-unit validation when the foreign key does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTimeUnitDoesNotExist_ShouldReturnTimeUnitNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketComplexityCommandTestContext(companyId);
        var command = CreateValidCommand();

        context.TicketComplexityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketComplexity>());
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(command.TimeUnitId)).ReturnsAsync((TimeUnit?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TIME_UNIT_NOT_FOUND");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the insert does not affect any rows.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketComplexityCommandTestContext(companyId);
        var command = CreateValidCommand();

        context.TicketComplexityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketComplexity>());
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(command.TimeUnitId)).ReturnsAsync(new TimeUnit());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
    }

    /// <summary>
    /// Verifies the successful creation flow and the normalized response payload.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateEntityAndReturnSuccess()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketComplexityCommandTestContext(companyId);
        var command = CreateValidCommand(name: "  Complex  ", description: "  Needs more time  ");
        TicketComplexity? insertedEntity = null;

        context.TicketComplexityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketComplexity>());
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(command.TimeUnitId)).ReturnsAsync(new TimeUnit());
        context.TicketComplexityRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<TicketComplexity>()))
            .Callback<TicketComplexity>(entity => insertedEntity = entity)
            .ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket complexity created successfully.");
        insertedEntity.Should().NotBeNull();
        insertedEntity!.CompanyId.Should().Be(companyId);
        insertedEntity.Name.Should().Be("Complex");
        insertedEntity.Description.Should().Be("Needs more time");
        insertedEntity.Code.Should().Be(command.Code);
        insertedEntity.ResolutionTimeUnits.Should().Be(command.ResolutionTimeUnits);
        insertedEntity.TimeUnitId.Should().Be(command.TimeUnitId);

        response.Data.Should().NotBeNull();
        response.Data!.CompanyId.Should().Be(companyId);
        response.Data.Name.Should().Be("Complex");
        response.Data.Description.Should().Be("Needs more time");
    }

    /// <summary>
    /// Creates a valid create command for ticket complexity scenarios.
    /// </summary>
    private CreateTicketComplexityCommand CreateValidCommand(
        string name = "Standard",
        string? description = "Default description",
        int code = 10,
        int resolutionTimeUnits = 2)
    {
        return new CreateTicketComplexityCommand
        {
            Name = name,
            Description = description,
            Code = code,
            ResolutionTimeUnits = resolutionTimeUnits,
            TimeUnitId = _fixture.Create<Guid>(),
            IsActive = true
        };
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the create handler.
    /// </summary>
    private sealed class CreateTicketComplexityCommandTestContext
    {
        public CreateTicketComplexityCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            TicketComplexityRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketComplexity>())).ReturnsAsync(true);
            TimeUnitRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TimeUnit>())).ReturnsAsync(true);
            SetupRepository(UnitOfWorkMock, TicketComplexityRepositoryMock);
            SetupRepository(UnitOfWorkMock, TimeUnitRepositoryMock);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketComplexity>> TicketComplexityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TimeUnit>> TimeUnitRepositoryMock { get; } = new();

        public CreateTicketComplexityCommandHandler CreateHandler()
        {
            return new CreateTicketComplexityCommandHandler(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
        }

        private static void SetupRepository<TEntity>(Mock<IUnitOfWork> unitOfWorkMock, Mock<IGenericRepository<TEntity>> repositoryMock)
            where TEntity : class
        {
            unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
        }
    }
}
