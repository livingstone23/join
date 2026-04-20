using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketStatuses.Commands.CreateTicketStatus;

/// <summary>
/// Contains the unit tests for the ticket status creation command.
/// These tests verify tenant protection, duplicate validation, persistence failure,
/// and the successful creation flow.
/// </summary>
public sealed class CreateTicketStatusCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the CompanyId claim is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new CreateTicketStatusCommandTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
    }

    /// <summary>
    /// Verifies the duplicate-name branch using case-insensitive comparison.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ShouldReturnNameInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketStatusCommandTestContext(companyId);
        var request = CreateValidCommand();

        context.TicketStatusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new TicketStatus { Name = request.Name.Trim().ToUpperInvariant(), Code = 999, GcRecord = 0 }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_STATUS_NAME_IN_USE");
        response.Errors.Should().Contain("Another active ticket status already uses the same name.");
    }

    /// <summary>
    /// Verifies the duplicate-code branch when another active status already uses the requested code.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeAlreadyExists_ShouldReturnCodeInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketStatusCommandTestContext(companyId);
        var request = CreateValidCommand();

        context.TicketStatusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new TicketStatus { Name = "Different", Code = request.Code, GcRecord = 0 }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_STATUS_CODE_IN_USE");
        response.Errors.Should().Contain("Another active ticket status already uses the same code.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketStatusCommandTestContext(companyId);
        var request = CreateValidCommand();

        context.TicketStatusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<TicketStatus>());

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
        response.Errors.Should().Contain("No records were affected while creating the ticket status.");
    }

    /// <summary>
    /// Verifies the happy path when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateTicketStatusAndReturnDto()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketStatusCommandTestContext(companyId);
        var request = CreateValidCommand();

        context.TicketStatusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<TicketStatus>());

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket status created successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.CompanyId.Should().Be(companyId);
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.Name.Should().Be("Open");
        response.Data.Description.Should().Be("Main workflow status");
        response.Data.Code.Should().Be(10);
        response.Data.IsInitial.Should().BeTrue();

        context.TicketStatusRepositoryMock.Verify(x => x.InsertAsync(It.Is<TicketStatus>(status =>
            status.CompanyId == companyId
            && status.Name == "Open"
            && status.Description == "Main workflow status"
            && status.Code == 10)), Times.Once);
    }

    /// <summary>
    /// Creates a valid create command for the ticket status flow.
    /// </summary>
    private CreateTicketStatusCommand CreateValidCommand()
    {
        return _fixture.Build<CreateTicketStatusCommand>()
            .With(x => x.Name, "  Open  ")
            .With(x => x.Description, "  Main workflow status  ")
            .With(x => x.Code, 10)
            .With(x => x.IsActive, true)
            .With(x => x.IsInitial, true)
            .With(x => x.IsPaused, false)
            .With(x => x.IsFinal, false)
            .Create();
    }

    /// <summary>
    /// Creates a generic repository mock to reduce arrange noise.
    /// </summary>
    private static Mock<IGenericRepository<TEntity>> CreateRepositoryMock<TEntity>() where TEntity : class
    {
        return new Mock<IGenericRepository<TEntity>>();
    }

    /// <summary>
    /// Registers a generic repository in the mocked unit of work.
    /// </summary>
    private static void SetupRepository<TEntity>(Mock<IUnitOfWork> unitOfWorkMock, Mock<IGenericRepository<TEntity>> repositoryMock)
        where TEntity : class
    {
        unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the create handler.
    /// </summary>
    private sealed class CreateTicketStatusCommandTestContext
    {
        public CreateTicketStatusCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            SetupRepository(UnitOfWorkMock, TicketStatusRepositoryMock);
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketStatus>> TicketStatusRepositoryMock { get; } = CreateRepositoryMock<TicketStatus>();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = CreateRepositoryMock<Company>();

        public CreateTicketStatusCommandHandler CreateHandler()
        {
            return new CreateTicketStatusCommandHandler(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
        }
    }
}
