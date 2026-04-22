using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;
using JOIN.Domain.Admin;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.IdentificationTypes.Commands.UpdateIdentificationType;

/// <summary>
/// Contains the unit tests for the identification type update command.
/// These tests verify not-found protection, duplicate-name validation,
/// persistence failures, and the successful update flow.
/// </summary>
public sealed class UpdateIdentificationTypeCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the requested identification type does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIdentificationTypeDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var request = CreateValidCommand(_fixture.Create<Guid>());
        var context = new UpdateIdentificationTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync((IdentificationType?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("IDENTIFICATION_TYPE_NOT_FOUND");
        response.Errors.Should().Contain("Identification type not found.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<IdentificationType>()), Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when the record was already soft-deleted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIdentificationTypeIsSoftDeleted_ShouldReturnNotFoundError()
    {
        // Arrange
        var request = CreateValidCommand(_fixture.Create<Guid>());
        var context = new UpdateIdentificationTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(new IdentificationType
            {
                Name = "Legacy",
                GcRecord = 20260420
            });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("IDENTIFICATION_TYPE_NOT_FOUND");
        response.Errors.Should().Contain("Identification type not found.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<IdentificationType>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherIdentificationTypeUsesSameName_ShouldReturnNameInUseError()
    {
        // Arrange
        var entity = new IdentificationType
        {
            Name = "Legacy",
            Description = "Old description",
            ValidationPattern = "old",
            IsActive = true,
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id);
        var context = new UpdateIdentificationTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                entity,
                new IdentificationType
                {
                    Name = request.Name.Trim().ToUpperInvariant(),
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
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<IdentificationType>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var entity = new IdentificationType
        {
            Name = "Legacy",
            Description = "Old description",
            ValidationPattern = "old",
            IsActive = false,
            GcRecord = 0,
            Created = new DateTime(2026, 4, 18, 10, 30, 0, DateTimeKind.Utc)
        };

        var request = CreateValidCommand(entity.Id);
        var context = new UpdateIdentificationTypeCommandTestContext();

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
        response.Errors.Should().Contain("No records were affected while updating the identification type.");
    }

    /// <summary>
    /// Verifies the happy path and normalization behavior when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateIdentificationTypeAndReturnDto()
    {
        // Arrange
        var entity = new IdentificationType
        {
            Name = "Legacy",
            Description = "Old description",
            ValidationPattern = "old",
            IsActive = false,
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id);
        var context = new UpdateIdentificationTypeCommandTestContext();

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
        response.Message.Should().Be("Identification type updated successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(entity.Id);
        response.Data.Name.Should().Be("Passport");
        response.Data.Description.Should().Be("International document");
        response.Data.ValidationPattern.Should().Be("^[A-Z0-9]{6,20}$");
        response.Data.IsActive.Should().BeTrue();
        response.Data.CreatedAt.Should().Be(entity.Created);

        entity.Name.Should().Be("Passport");
        entity.Description.Should().Be("International document");
        entity.ValidationPattern.Should().Be("^[A-Z0-9]{6,20}$");
        entity.IsActive.Should().BeTrue();

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that blank optional fields are normalized to null during update.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOptionalFieldsAreBlank_ShouldNormalizeOptionalFieldsToNull()
    {
        // Arrange
        var entity = new IdentificationType
        {
            Name = "Legacy",
            Description = "Old description",
            ValidationPattern = "old",
            IsActive = false,
            GcRecord = 0
        };

        var request = new UpdateIdentificationTypeCommand
        {
            Id = entity.Id,
            Name = "  Passport  ",
            Description = "   ",
            ValidationPattern = "   ",
            IsActive = true
        };

        var context = new UpdateIdentificationTypeCommandTestContext();

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
        entity.Name.Should().Be("Passport");
        entity.Description.Should().BeNull();
        entity.ValidationPattern.Should().BeNull();
        response.Data.Should().NotBeNull();
        response.Data!.Description.Should().BeNull();
        response.Data.ValidationPattern.Should().BeNull();
    }

    /// <summary>
    /// Creates a valid command instance for the identification type update flow.
    /// </summary>
    private static UpdateIdentificationTypeCommand CreateValidCommand(Guid id)
    {
        return new UpdateIdentificationTypeCommand
        {
            Id = id,
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
    /// Holds the reusable mocks and helper factory for the update handler.
    /// </summary>
    private sealed class UpdateIdentificationTypeCommandTestContext
    {
        public UpdateIdentificationTypeCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<IdentificationType>> RepositoryMock { get; } = new();

        public UpdateIdentificationTypeCommandHandler CreateHandler()
        {
            return new UpdateIdentificationTypeCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
