using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Provinces.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Provinces.Commands.CreateProvince;

/// <summary>
/// Contains the unit tests for the province creation command.
/// These tests verify foreign-key validation, duplicate protections, and the success path.
/// </summary>
public sealed class CreateProvinceCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the validation branch when the requested country does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCountryDoesNotExist_ShouldReturnCountryNotFoundError()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var context = new CreateProvinceCommandTestContext();
        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync((Country?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(countryId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_COUNTRY_NOT_FOUND");
        response.Errors.Should().Contain("The specified CountryId does not exist.");
    }

    /// <summary>
    /// Verifies the validation branch when the requested region does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRegionDoesNotExist_ShouldReturnRegionNotFoundError()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var regionId = _fixture.Create<Guid>();
        var context = new CreateProvinceCommandTestContext();

        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync((Region?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(countryId) with { RegionId = regionId }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_REGION_NOT_FOUND");
        response.Errors.Should().Contain("The specified RegionId does not exist.");
    }

    /// <summary>
    /// Verifies the integrity branch when the selected region belongs to a different country.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRegionBelongsToDifferentCountry_ShouldReturnMismatchError()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var regionId = _fixture.Create<Guid>();
        var context = new CreateProvinceCommandTestContext();

        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync(CreateRegion(regionId, _fixture.Create<Guid>()));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(countryId) with { RegionId = regionId }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_REGION_COUNTRY_MISMATCH");
        response.Errors.Should().Contain("The specified region does not belong to the selected country.");
    }

    /// <summary>
    /// Verifies the duplicate-code branch using a case-insensitive comparison within the same country.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeAlreadyExists_ShouldReturnCodeInUseError()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var context = new CreateProvinceCommandTestContext();
        var request = CreateValidCommand(countryId);

        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.ProvinceRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            new Province { Name = "Managua", Code = "MN", CountryId = countryId, GcRecord = 0 }
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request with { Code = " mn " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_CODE_IN_USE");
        response.Errors.Should().Contain("Another active province already uses the same code for this country.");
    }

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison within the same country.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ShouldReturnNameInUseError()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var context = new CreateProvinceCommandTestContext();
        var request = CreateValidCommand(countryId);

        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.ProvinceRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            new Province { Name = "Managua", Code = "MG", CountryId = countryId, GcRecord = 0 }
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request with { Name = "  Managua  " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_NAME_IN_USE");
        response.Errors.Should().Contain("Another active province already uses the same name for this country.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the save operation affects no rows.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var context = new CreateProvinceCommandTestContext();

        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.ProvinceRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Province>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(countryId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
        response.Errors.Should().Contain("No records were affected while creating the province.");
    }

    /// <summary>
    /// Verifies the successful creation flow and value normalization.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateProvinceAndReturnDto()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var regionId = _fixture.Create<Guid>();
        var context = new CreateProvinceCommandTestContext();
        Province? insertedEntity = null;

        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync(CreateRegion(regionId, countryId));
        context.ProvinceRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Province>());
        context.ProvinceRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<Province>()))
            .Callback<Province>(entity => insertedEntity = entity)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(
            CreateValidCommand(countryId) with { Name = "  Managua  ", Code = " mn ", RegionId = regionId },
            CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Province created successfully.");
        insertedEntity.Should().NotBeNull();
        insertedEntity!.Name.Should().Be("Managua");
        insertedEntity.Code.Should().Be("MN");
        insertedEntity.CountryId.Should().Be(countryId);
        insertedEntity.RegionId.Should().Be(regionId);
        response.Data.Should().NotBeNull();
        response.Data!.CountryName.Should().Be("Nicaragua");
        response.Data.RegionName.Should().Be("Pacific");
    }

    /// <summary>
    /// Creates a valid command for province creation scenarios.
    /// </summary>
    private static CreateProvinceCommand CreateValidCommand(Guid countryId)
    {
        return new CreateProvinceCommand
        {
            Name = "Managua",
            Code = "MN",
            CountryId = countryId,
            RegionId = null
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
    /// Creates a region entity for foreign-key validation scenarios.
    /// </summary>
    private static Region CreateRegion(Guid regionId, Guid countryId)
    {
        var entity = new Region
        {
            Name = "Pacific",
            Code = "PA",
            CountryId = countryId
        };

        typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(entity, regionId);
        return entity;
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the create handler.
    /// </summary>
    private sealed class CreateProvinceCommandTestContext
    {
        public CreateProvinceCommandTestContext()
        {
            ProvinceRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<Province>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<Province>()).Returns(ProvinceRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Country>()).Returns(CountryRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Region>()).Returns(RegionRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Province>> ProvinceRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Country>> CountryRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Region>> RegionRepositoryMock { get; } = new();

        public CreateProvinceCommandHandler CreateHandler()
        {
            return new CreateProvinceCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
