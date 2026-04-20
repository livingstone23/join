using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TimeUnits.Commands;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TimeUnits.Commands.CreateTimeUnit;

/// <summary>
/// Contains the unit tests for the time unit create command.
/// These tests verify tenant protection, duplicate validation, and successful persistence.
/// </summary>
public sealed class CreateTimeUnitCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the CompanyId claim is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new CreateTimeUnitCommandTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    /// <summary>
    /// Verifies the duplicate-name branch using trimmed and case-insensitive matching.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameIsAlreadyUsed_ShouldReturnNameInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTimeUnitCommandTestContext(companyId);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
        [
            new TimeUnit { Name = "Hours", Code = 1, GcRecord = 0 }
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(name: "  hours  "), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TIME_UNIT_NAME_IN_USE");
    }

    /// <summary>
    /// Verifies the duplicate-code branch when another active item already uses the requested code.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeIsAlreadyUsed_ShouldReturnCodeInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTimeUnitCommandTestContext(companyId);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
        [
            new TimeUnit { Name = "Days", Code = 24, GcRecord = 0 }
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(code: 24), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TIME_UNIT_CODE_IN_USE");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the insert does not affect any rows.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTimeUnitCommandTestContext(companyId);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TimeUnit>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
    }

    /// <summary>
    /// Verifies the successful creation flow and normalized response payload.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateEntityAndReturnSuccess()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTimeUnitCommandTestContext(companyId);
        TimeUnit? insertedEntity = null;

        context.TimeUnitRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TimeUnit>());
        context.TimeUnitRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<TimeUnit>()))
            .Callback<TimeUnit>(entity => insertedEntity = entity)
            .ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(name: "  Hours  ", code: 1), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Time unit created successfully.");
        insertedEntity.Should().NotBeNull();
        insertedEntity!.CompanyId.Should().Be(companyId);
        insertedEntity.Name.Should().Be("Hours");
        insertedEntity.Code.Should().Be(1);
        response.Data.Should().NotBeNull();
        response.Data!.CompanyId.Should().Be(companyId);
        response.Data.Name.Should().Be("Hours");
        response.Data.Code.Should().Be(1);
    }

    /// <summary>
    /// Creates a valid create command for time unit scenarios.
    /// </summary>
    private static CreateTimeUnitCommand CreateValidCommand(string name = "Hours", int code = 1)
    {
        return new CreateTimeUnitCommand
        {
            Name = name,
            Code = code,
            IsActive = true
        };
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the create handler.
    /// </summary>
    private sealed class CreateTimeUnitCommandTestContext
    {
        public CreateTimeUnitCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            TimeUnitRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TimeUnit>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<TimeUnit>()).Returns(TimeUnitRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TimeUnit>> TimeUnitRepositoryMock { get; } = new();

        public CreateTimeUnitCommandHandler CreateHandler()
        {
            return new CreateTimeUnitCommandHandler(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
        }
    }
}
