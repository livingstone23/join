using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Regions.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Regions.Commands.UpdateRegion;

/// <summary>
/// Contains the unit tests for the region update command.
/// These tests verify not-found handling, duplicate validation, and successful persistence.
/// </summary>
public sealed class UpdateRegionCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the region does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRegionDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var regionId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var context = new UpdateRegionCommandTestContext();
        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync((Region?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(regionId, countryId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("REGION_NOT_FOUND");
        response.Errors.Should().Contain("Region not found.");
    }

    /// <summary>
    /// Verifies the validation branch when the requested country does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCountryDoesNotExist_ShouldReturnCountryNotFoundError()
    {
        // Arrange
        var regionId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var existingRegion = CreateRegion(regionId, countryId);
        var context = new UpdateRegionCommandTestContext();

        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync(existingRegion);
        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync((Country?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(regionId, countryId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("REGION_COUNTRY_NOT_FOUND");
        response.Errors.Should().Contain("The specified CountryId does not exist.");
    }

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison within the same country.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherRegionUsesSameName_ShouldReturnNameInUseError()
    {
        // Arrange
        var regionId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var existingRegion = CreateRegion(regionId, countryId);
        var context = new UpdateRegionCommandTestContext();

        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync(existingRegion);
        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            existingRegion,
            CreateRegion(_fixture.Create<Guid>(), countryId, name: "North", code: "AA")
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(regionId, countryId) with { Name = " north " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("REGION_NAME_IN_USE");
    }

    /// <summary>
    /// Verifies the duplicate-code branch using a case-insensitive comparison within the same country.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherRegionUsesSameCode_ShouldReturnCodeInUseError()
    {
        // Arrange
        var regionId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var existingRegion = CreateRegion(regionId, countryId);
        var context = new UpdateRegionCommandTestContext();

        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync(existingRegion);
        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            existingRegion,
            CreateRegion(_fixture.Create<Guid>(), countryId, name: "Central", code: "CC")
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(regionId, countryId) with { Code = " cc " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("REGION_CODE_IN_USE");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the save operation affects no rows.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var regionId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var existingRegion = CreateRegion(regionId, countryId, name: "Old", code: "OL");
        var context = new UpdateRegionCommandTestContext();

        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync(existingRegion);
        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { existingRegion });
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(regionId, countryId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UPDATE_FAILED");
        response.Errors.Should().Contain("No records were affected while updating the region.");
    }

    /// <summary>
    /// Verifies the successful update flow and value normalization.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateRegionAndReturnDto()
    {
        // Arrange
        var regionId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var existingRegion = CreateRegion(regionId, countryId, name: "Old", code: "OL");
        var context = new UpdateRegionCommandTestContext();

        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync(existingRegion);
        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { existingRegion });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(regionId, countryId) with { Name = "  North  ", Code = " ni " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Region updated successfully.");
        existingRegion.Name.Should().Be("North");
        existingRegion.Code.Should().Be("NI");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(regionId);
        response.Data.CountryName.Should().Be("Nicaragua");
        context.RegionRepositoryMock.Verify(x => x.UpdateAsync(existingRegion), Times.Once);
    }

    /// <summary>
    /// Creates a valid command for update scenarios.
    /// </summary>
    private static UpdateRegionCommand CreateValidCommand(Guid regionId, Guid countryId)
    {
        return new UpdateRegionCommand
        {
            Id = regionId,
            Name = "North",
            Code = "NI",
            CountryId = countryId
        };
    }

    /// <summary>
    /// Creates a region entity for update scenarios.
    /// </summary>
    private static Region CreateRegion(Guid regionId, Guid countryId, string name = "South", string? code = "SO")
    {
        var entity = new Region
        {
            Name = name,
            Code = code,
            CountryId = countryId
        };

        typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(entity, regionId);
        return entity;
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
    /// Holds the reusable mocks and helper factory for the update handler.
    /// </summary>
    private sealed class UpdateRegionCommandTestContext
    {
        public UpdateRegionCommandTestContext()
        {
            RegionRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Region>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<Region>()).Returns(RegionRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Country>()).Returns(CountryRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Region>> RegionRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Country>> CountryRepositoryMock { get; } = new();

        public UpdateRegionCommandHandler CreateHandler()
        {
            return new UpdateRegionCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
