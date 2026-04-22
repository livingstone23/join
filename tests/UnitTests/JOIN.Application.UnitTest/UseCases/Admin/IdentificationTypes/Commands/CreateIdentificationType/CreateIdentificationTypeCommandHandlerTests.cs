using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;
using JOIN.Domain.Admin;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.IdentificationTypes.Commands.CreateIdentificationType;

/// <summary>
/// Contains the unit tests for the identification type creation command.
/// These tests verify duplicate-name protection, persistence failures,
/// normalization, and the successful creation flow.
/// </summary>
public sealed class CreateIdentificationTypeCommandHandlerTests
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
        var context = new CreateIdentificationTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new IdentificationType
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
        response.Message.Should().Be("IDENTIFICATION_TYPE_NAME_IN_USE");
        response.Errors.Should().Contain("Another active identification type already uses the same name.");
        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<IdentificationType>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateIdentificationTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<IdentificationType>());

        context.RepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<IdentificationType>()))
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
        response.Errors.Should().Contain("No records were affected while creating the identification type.");
    }

    /// <summary>
    /// Verifies the happy path and normalization behavior when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateIdentificationTypeAndReturnDto()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateIdentificationTypeCommandTestContext();
        IdentificationType? insertedEntity = null;

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<IdentificationType>());

        context.RepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<IdentificationType>()))
            .Callback<IdentificationType>(entity => insertedEntity = entity)
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Identification type created successfully.");
        response.Data.Should().NotBeNull();
        insertedEntity.Should().NotBeNull();
        response.Data!.Id.Should().Be(insertedEntity!.Id);
        response.Data!.Name.Should().Be("Passport");
        response.Data.Description.Should().Be("International document");
        response.Data.ValidationPattern.Should().Be("^[A-Z0-9]{6,20}$");
        response.Data.IsActive.Should().BeTrue();
        response.Data.CreatedAt.Should().Be(insertedEntity.Created);

        context.RepositoryMock.Verify(x => x.InsertAsync(It.Is<IdentificationType>(entity =>
            entity.Name == "Passport"
            && entity.Description == "International document"
            && entity.ValidationPattern == "^[A-Z0-9]{6,20}$"
            && entity.IsActive)), Times.Once);

        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that blank optional fields are normalized to null during creation.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOptionalFieldsAreBlank_ShouldNormalizeOptionalFieldsToNull()
    {
        // Arrange
        var request = new CreateIdentificationTypeCommand
        {
            Name = "  Passport  ",
            Description = "   ",
            ValidationPattern = null,
            IsActive = true
        };
        var context = new CreateIdentificationTypeCommandTestContext();
        IdentificationType? insertedEntity = null;

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<IdentificationType>());

        context.RepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<IdentificationType>()))
            .Callback<IdentificationType>(entity => insertedEntity = entity)
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        insertedEntity.Should().NotBeNull();
        insertedEntity!.Name.Should().Be("Passport");
        insertedEntity.Description.Should().BeNull();
        insertedEntity.ValidationPattern.Should().BeNull();
        response.Data.Should().NotBeNull();
        response.Data!.Description.Should().BeNull();
        response.Data.ValidationPattern.Should().BeNull();
    }

    /// <summary>
    /// Creates a valid command instance for the identification type creation flow.
    /// </summary>
    private static CreateIdentificationTypeCommand CreateValidCommand()
    {
        return new CreateIdentificationTypeCommand
        {
            Name = "  Passport  ",
            Description = "  International document  ",
            ValidationPattern = "  ^[A-Z0-9]{6,20}$  ",
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
    private sealed class CreateIdentificationTypeCommandTestContext
    {
        public CreateIdentificationTypeCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<IdentificationType>> RepositoryMock { get; } = new();

        public CreateIdentificationTypeCommandHandler CreateHandler()
        {
            return new CreateIdentificationTypeCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
