using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Countries.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Countries.Commands.CreateCountry;

/// <summary>
/// Contains the unit tests for the country creation command handler.
/// These tests verify duplicate ISO code protection, persistence failures,
/// normalization, and the successful creation flow.
/// </summary>
public sealed class CreateCountryCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the duplicate ISO code branch (case-insensitive after ToUpperInvariant normalization).
    /// </summary>
    [Fact]
    public async Task Handle_WhenIsoCodeAlreadyExists_ShouldReturnIsoCodeInUseError()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateCountryCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new Country
                {
                    IsoCode = request.IsoCode.Trim().ToUpperInvariant(),
                    Name = "Old Panama",
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
        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Country>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateCountryCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Country>());

        context.RepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<Country>()))
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
        response.Errors.Should().Contain("No records were affected while creating the country.");
    }

    /// <summary>
    /// Verifies the happy path and normalization behavior when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateCountryAndReturnDto()
    {
        // Arrange
        var request = CreateValidCommand();
        var context = new CreateCountryCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Country>());

        context.RepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<Country>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Country created successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Name.Should().Be("Panama");
        response.Data.IsoCode.Should().Be("PA");

        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Country>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid command instance for the country creation flow.
    /// </summary>
    private static CreateCountryCommand CreateValidCommand()
    {
        return new CreateCountryCommand
        {
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
    /// Holds the reusable mocks and helper factory for the create handler.
    /// </summary>
    private sealed class CreateCountryCommandTestContext
    {
        public CreateCountryCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Country>> RepositoryMock { get; } = new();

        public CreateCountryCommandHandler CreateHandler()
        {
            return new CreateCountryCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
