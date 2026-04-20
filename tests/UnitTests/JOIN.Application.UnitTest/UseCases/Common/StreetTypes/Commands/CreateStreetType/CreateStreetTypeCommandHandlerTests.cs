using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.StreetTypes.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.StreetTypes.Commands.CreateStreetType;

/// <summary>
/// Contains the unit tests for the street type creation command handler.
/// These tests verify duplicate-name protection, duplicate-abbreviation protection,
/// persistence failures, and the successful creation flow.
/// </summary>
public sealed class CreateStreetTypeCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the duplicate-name branch when another active street type uses the same name.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ShouldReturnNameInUseError()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateStreetTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new StreetType
                {
                    Name = request.Name.Trim(),
                    Abbreviation = "Other",
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
        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<StreetType>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate-abbreviation branch when another active street type uses the same abbreviation.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAbbreviationAlreadyExists_ShouldReturnAbbreviationInUseError()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateStreetTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
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
        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<StreetType>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateStreetTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<StreetType>());

        context.RepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<StreetType>()))
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
        response.Errors.Should().Contain("No records were affected while creating the street type.");
    }

    /// <summary>
    /// Verifies the happy path and normalization behavior when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateStreetTypeAndReturnDto()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateStreetTypeCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<StreetType>());

        context.RepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<StreetType>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Street type created successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Name.Should().Be("Avenue");
        response.Data.Abbreviation.Should().Be("Ave");
        response.Data.IsActive.Should().BeTrue();

        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<StreetType>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid command instance for the street type creation flow.
    /// </summary>
    private static CreateStreetTypeCommand CreateValidCommand()
    {
        return new CreateStreetTypeCommand
        {
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
    /// Holds the reusable mocks and helper factory for the create handler.
    /// </summary>
    private sealed class CreateStreetTypeCommandTestContext
    {
        public CreateStreetTypeCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<StreetType>> RepositoryMock { get; } = new();

        public CreateStreetTypeCommandHandler CreateHandler()
        {
            return new CreateStreetTypeCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
