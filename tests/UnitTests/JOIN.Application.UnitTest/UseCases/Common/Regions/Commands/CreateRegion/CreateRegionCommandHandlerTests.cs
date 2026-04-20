using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Regions.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Regions.Commands.CreateRegion;

/// <summary>
/// Contains the unit tests for the region creation command.
/// These tests verify foreign-key validation, duplicate protections, and the success path.
/// </summary>
public sealed class CreateRegionCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the validation branch when the requested country does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCountryDoesNotExist_ShouldReturnCountryNotFoundError()
    {
        // Arrange
        var context = new CreateRegionCommandTestContext();
        var request = CreateValidCommand(_fixture.Create<Guid>());
        context.CountryRepositoryMock.Setup(x => x.GetAsync(request.CountryId)).ReturnsAsync((Country?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("REGION_COUNTRY_NOT_FOUND");
        response.Errors.Should().Contain("The specified CountryId does not exist.");
    }

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison within the same country.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ShouldReturnNameInUseError()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var context = new CreateRegionCommandTestContext();
        var request = CreateValidCommand(countryId);

        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            new Region { Name = "North", Code = "AA", CountryId = countryId, GcRecord = 0 }
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request with { Name = "  north  " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("REGION_NAME_IN_USE");
        response.Errors.Should().Contain("Another active region already uses the same name for this country.");
    }

    /// <summary>
    /// Verifies the duplicate-code branch using a case-insensitive comparison within the same country.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeAlreadyExists_ShouldReturnCodeInUseError()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var context = new CreateRegionCommandTestContext();
        var request = CreateValidCommand(countryId);

        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            new Region { Name = "Central", Code = "CA", CountryId = countryId, GcRecord = 0 }
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request with { Code = " ca " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("REGION_CODE_IN_USE");
        response.Errors.Should().Contain("Another active region already uses the same code for this country.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the save operation affects no rows.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var context = new CreateRegionCommandTestContext();
        var request = CreateValidCommand(countryId);

        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Region>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
        response.Errors.Should().Contain("No records were affected while creating the region.");
    }

    /// <summary>
    /// Verifies the successful creation flow and value normalization.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateRegionAndReturnDto()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var context = new CreateRegionCommandTestContext();
        var request = CreateValidCommand(countryId) with { Name = "  North  ", Code = " ni " };
        Region? insertedEntity = null;

        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Region>());
        context.RegionRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<Region>()))
            .Callback<Region>(entity => insertedEntity = entity)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Region created successfully.");
        insertedEntity.Should().NotBeNull();
        insertedEntity!.Name.Should().Be("North");
        insertedEntity.Code.Should().Be("NI");
        insertedEntity.CountryId.Should().Be(countryId);
        response.Data.Should().NotBeNull();
        response.Data!.Name.Should().Be("North");
        response.Data.Code.Should().Be("NI");
        response.Data.CountryName.Should().Be("Nicaragua");
    }

    /// <summary>
    /// Creates a valid command for region creation scenarios.
    /// </summary>
    private static CreateRegionCommand CreateValidCommand(Guid countryId)
    {
        return new CreateRegionCommand
        {
            Name = "North",
            Code = "NI",
            CountryId = countryId
        };
    }

    /// <summary>
    /// Creates a country entity for foreign-key validation scenarios.
    /// </summary>
    private static Country CreateCountry(Guid countryId)
    {
        var entity = new Country
        {
            Name = "Nicaragua",
            IsoCode = "NI"
        };

        typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(entity, countryId);
        return entity;
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the create handler.
    /// </summary>
    private sealed class CreateRegionCommandTestContext
    {
        public CreateRegionCommandTestContext()
        {
            RegionRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<Region>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<Region>()).Returns(RegionRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Country>()).Returns(CountryRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Region>> RegionRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Country>> CountryRepositoryMock { get; } = new();

        public CreateRegionCommandHandler CreateHandler()
        {
            return new CreateRegionCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
