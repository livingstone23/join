using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Countries.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Countries.Commands.UpdateCountry;

/// <summary>
/// Contains the unit tests for the country update command handler.
/// These tests verify not-found protection, duplicate ISO code validation,
/// persistence failures, and the successful update flow.
/// </summary>
public sealed class UpdateCountryCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the requested country does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCountryDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var request = CreateValidCommand(_fixture.Create<Guid>());
        var context = new UpdateCountryCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync((Country?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COUNTRY_NOT_FOUND");
        response.Errors.Should().Contain("Country not found.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Country>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate ISO code branch when another country already uses the same code.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherCountryUsesSameIsoCode_ShouldReturnIsoCodeInUseError()
    {
        // Arrange
        var entity = new Country
        {
            Name = "Old Name",
            IsoCode = "MX",
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id);
        var context = new UpdateCountryCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                entity,
                new Country
                {
                    IsoCode = request.IsoCode.Trim().ToUpperInvariant(),
                    Name = "Another Country",
                    GcRecord = 0
                }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COUNTRY_ISO_CODE_IN_USE");
        response.Errors.Should().Contain("Another active country already uses the same ISO code.");
        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Country>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var entity = new Country
        {
            Name = "Old Name",
            IsoCode = "MX",
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id);
        var context = new UpdateCountryCommandTestContext();

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
        response.Errors.Should().Contain("No records were affected while updating the country.");
    }

    /// <summary>
    /// Verifies the happy path and normalization behavior when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateCountryAndReturnDto()
    {
        // Arrange
        var entity = new Country
        {
            Name = "Old Name",
            IsoCode = "MX",
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id);
        var context = new UpdateCountryCommandTestContext();

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
        response.Message.Should().Be("Country updated successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(entity.Id);
        response.Data.Name.Should().Be("Panama");
        response.Data.IsoCode.Should().Be("PA");

        entity.Name.Should().Be("Panama");
        entity.IsoCode.Should().Be("PA");

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid command instance for the country update flow.
    /// </summary>
    private static UpdateCountryCommand CreateValidCommand(Guid id)
    {
        return new UpdateCountryCommand
        {
            Id = id,
            Name = "  Panama  ",
            IsoCode = "  pa  "
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
    private sealed class UpdateCountryCommandTestContext
    {
        public UpdateCountryCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Country>> RepositoryMock { get; } = new();

        public UpdateCountryCommandHandler CreateHandler()
        {
            return new UpdateCountryCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
