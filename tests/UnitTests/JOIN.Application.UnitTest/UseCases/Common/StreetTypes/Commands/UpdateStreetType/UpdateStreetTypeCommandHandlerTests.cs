using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.StreetTypes.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.StreetTypes.Commands.UpdateStreetType;

/// <summary>
/// Contains the unit tests for the street type update command handler.
/// These tests verify not-found protection, duplicate-name/abbreviation validation,
/// persistence failures, and the successful update flow.
/// </summary>
public sealed class UpdateStreetTypeCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the requested street type does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStreetTypeDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var request = CreateValidCommand(_fixture.Create<Guid>());
        var context = new UpdateStreetTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync((StreetType?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("STREETTYPE_NOT_FOUND");
        response.Errors.Should().Contain("Street type not found.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<StreetType>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate-name branch when another active street type uses the same name.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherStreetTypeUsesSameName_ShouldReturnNameInUseError()
    {
        // Arrange
        var entity = new StreetType
        {
            Name = "Road",
            Abbreviation = "Rd",
            IsActive = true,
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id);
        var context = new UpdateStreetTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                entity,
                new StreetType
                {
                    Name = request.Name.Trim(),
                    Abbreviation = "Diff",
                    GcRecord = 0
                }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("STREETTYPE_NAME_IN_USE");
        response.Errors.Should().Contain("Another active street type already uses the same name.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<StreetType>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate-abbreviation branch when another active street type uses the same abbreviation.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherStreetTypeUsesSameAbbreviation_ShouldReturnAbbreviationInUseError()
    {
        // Arrange
        var entity = new StreetType
        {
            Name = "Road",
            Abbreviation = "Rd",
            IsActive = true,
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id);
        var context = new UpdateStreetTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                entity,
                new StreetType
                {
                    Name = "DifferentName",
                    Abbreviation = request.Abbreviation.Trim(),
                    GcRecord = 0
                }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("STREETTYPE_ABBREVIATION_IN_USE");
        response.Errors.Should().Contain("Another active street type already uses the same abbreviation.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<StreetType>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var entity = new StreetType
        {
            Name = "Road",
            Abbreviation = "Rd",
            IsActive = false,
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id);
        var context = new UpdateStreetTypeCommandTestContext();

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
        response.Errors.Should().Contain("No records were affected while updating the street type.");
    }

    /// <summary>
    /// Verifies the happy path and normalization behavior when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateStreetTypeAndReturnDto()
    {
        // Arrange
        var entity = new StreetType
        {
            Name = "Road",
            Abbreviation = "Rd",
            IsActive = false,
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id);
        var context = new UpdateStreetTypeCommandTestContext();

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
        response.Message.Should().Be("Street type updated successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(entity.Id);
        response.Data.Name.Should().Be("Avenue");
        response.Data.Abbreviation.Should().Be("Ave");
        response.Data.IsActive.Should().BeTrue();

        entity.Name.Should().Be("Avenue");
        entity.Abbreviation.Should().Be("Ave");
        entity.IsActive.Should().BeTrue();

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid command instance for the street type update flow.
    /// </summary>
    private static UpdateStreetTypeCommand CreateValidCommand(Guid id)
    {
        return new UpdateStreetTypeCommand
        {
            Id = id,
            Name = "  Avenue  ",
            Abbreviation = "  Ave  ",
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
    private sealed class UpdateStreetTypeCommandTestContext
    {
        public UpdateStreetTypeCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<StreetType>> RepositoryMock { get; } = new();

        public UpdateStreetTypeCommandHandler CreateHandler()
        {
            return new UpdateStreetTypeCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
