using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.SystemModules.Commands;
using JOIN.Domain.Admin;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.SystemModules.Commands.CreateSystemModule;

/// <summary>
/// Contains the unit tests for the system module creation command.
/// These tests verify duplicate-name protection, persistence failures,
/// normalization, and the successful creation flow.
/// </summary>
public sealed class CreateSystemModuleCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ShouldReturnNameInUseError()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateSystemModuleCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new SystemModule
                {
                    Name = request.Name.Trim().ToUpperInvariant(),
                    IsActive = true,
                    GcRecord = 0
                }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SYSTEM_MODULE_NAME_IN_USE");
        response.Errors.Should().Contain("Another active system module already uses the same name.");
        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<SystemModule>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no rows are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateSystemModuleCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<SystemModule>());

        context.RepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<SystemModule>()))
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
        response.Errors.Should().Contain("No records were affected while creating the system module.");
    }

    /// <summary>
    /// Verifies the happy path and normalization behavior when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateSystemModuleAndReturnDto()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateSystemModuleCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<SystemModule>());

        context.RepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<SystemModule>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("System module created successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Name.Should().Be("CRM");
        response.Data.Description.Should().Be("Customer management");
        response.Data.Icon.Should().Be("fa-users");
        response.Data.IsActive.Should().BeTrue();

        context.RepositoryMock.Verify(x => x.InsertAsync(It.Is<SystemModule>(entity =>
            entity.Name == "CRM"
            && entity.Description == "Customer management"
            && entity.Icon == "fa-users"
            && entity.IsActive)), Times.Once);
    }

    /// <summary>
    /// Creates a valid command instance for the creation flow.
    /// </summary>
    private static CreateSystemModuleCommand CreateValidCommand()
    {
        return new CreateSystemModuleCommand
        {
            Name = "  CRM  ",
            Description = "  Customer management  ",
            Icon = "  fa-users  ",
            IsActive = true
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
    private sealed class CreateSystemModuleCommandTestContext
    {
        public CreateSystemModuleCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<SystemModule>> RepositoryMock { get; } = new();

        public CreateSystemModuleCommandHandler CreateHandler()
        {
            return new CreateSystemModuleCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
