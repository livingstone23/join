using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Provinces.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Provinces.Commands.UpdateProvince;

/// <summary>
/// Contains the unit tests for the province update command.
/// These tests verify foreign-key validation, duplicate protections, and the success path.
/// </summary>
public sealed class UpdateProvinceCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the province does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProvinceDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var context = new UpdateProvinceCommandTestContext();
        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync((Province?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(provinceId, countryId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_NOT_FOUND");
        response.Errors.Should().Contain("Province not found.");
    }

    /// <summary>
    /// Verifies the validation branch when the requested country does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCountryDoesNotExist_ShouldReturnCountryNotFoundError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var context = new UpdateProvinceCommandTestContext();
        var province = CreateProvince(provinceId, countryId);

        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(province);
        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync((Country?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(provinceId, countryId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_COUNTRY_NOT_FOUND");
    }

    /// <summary>
    /// Verifies the validation branch when the requested region does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRegionDoesNotExist_ShouldReturnRegionNotFoundError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var regionId = _fixture.Create<Guid>();
        var context = new UpdateProvinceCommandTestContext();
        var province = CreateProvince(provinceId, countryId);

        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(province);
        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync((Region?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(provinceId, countryId) with { RegionId = regionId }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_REGION_NOT_FOUND");
    }

    /// <summary>
    /// Verifies the integrity branch when the selected region belongs to a different country.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRegionBelongsToDifferentCountry_ShouldReturnMismatchError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var regionId = _fixture.Create<Guid>();
        var context = new UpdateProvinceCommandTestContext();
        var province = CreateProvince(provinceId, countryId);

        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(province);
        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync(CreateRegion(regionId, _fixture.Create<Guid>()));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(provinceId, countryId) with { RegionId = regionId }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_REGION_COUNTRY_MISMATCH");
    }

    /// <summary>
    /// Verifies the duplicate-code branch using a case-insensitive comparison within the same country.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherProvinceUsesSameCode_ShouldReturnCodeInUseError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var context = new UpdateProvinceCommandTestContext();
        var province = CreateProvince(provinceId, countryId, name: "Old", code: "OL");

        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(province);
        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.ProvinceRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            province,
            CreateProvince(_fixture.Create<Guid>(), countryId, name: "Managua", code: "MN")
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(provinceId, countryId) with { Code = " mn " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_CODE_IN_USE");
    }

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison within the same country.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherProvinceUsesSameName_ShouldReturnNameInUseError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var context = new UpdateProvinceCommandTestContext();
        var province = CreateProvince(provinceId, countryId, name: "Old", code: "OL");

        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(province);
        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.ProvinceRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            province,
            CreateProvince(_fixture.Create<Guid>(), countryId, name: "Managua", code: "MG")
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(provinceId, countryId) with { Name = " Managua " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_NAME_IN_USE");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the save operation affects no rows.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var context = new UpdateProvinceCommandTestContext();
        var province = CreateProvince(provinceId, countryId, name: "Old", code: "OL");

        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(province);
        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.ProvinceRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { province });
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(provinceId, countryId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UPDATE_FAILED");
        response.Errors.Should().Contain("No records were affected while updating the province.");
    }

    /// <summary>
    /// Verifies the successful update flow and value normalization.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateProvinceAndReturnDto()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var countryId = _fixture.Create<Guid>();
        var regionId = _fixture.Create<Guid>();
        var context = new UpdateProvinceCommandTestContext();
        var province = CreateProvince(provinceId, countryId, name: "Old", code: "OL");

        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(province);
        context.CountryRepositoryMock.Setup(x => x.GetAsync(countryId)).ReturnsAsync(CreateCountry(countryId));
        context.RegionRepositoryMock.Setup(x => x.GetAsync(regionId)).ReturnsAsync(CreateRegion(regionId, countryId));
        context.ProvinceRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { province });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(
            CreateValidCommand(provinceId, countryId) with { Name = "  Managua  ", Code = " mn ", RegionId = regionId },
            CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Province updated successfully.");
        province.Name.Should().Be("Managua");
        province.Code.Should().Be("MN");
        province.RegionId.Should().Be(regionId);
        response.Data.Should().NotBeNull();
        response.Data!.CountryName.Should().Be("Nicaragua");
        response.Data.RegionName.Should().Be("Pacific");
        context.ProvinceRepositoryMock.Verify(x => x.UpdateAsync(province), Times.Once);
    }

    /// <summary>
    /// Creates a valid command for province update scenarios.
    /// </summary>
    private static UpdateProvinceCommand CreateValidCommand(Guid provinceId, Guid countryId)
    {
        return new UpdateProvinceCommand
        {
            Id = provinceId,
            Name = "Managua",
            Code = "MN",
            CountryId = countryId,
            RegionId = null
        };
    }

    /// <summary>
    /// Creates a province entity for update scenarios.
    /// </summary>
    private static Province CreateProvince(Guid provinceId, Guid countryId, string name = "León", string code = "LE")
    {
        var entity = new Province
        {
            Name = name,
            Code = code,
            CountryId = countryId,
            RegionId = null
        };

        typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(entity, provinceId);
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
    /// Holds the reusable mocks and helper factory for the update handler.
    /// </summary>
    private sealed class UpdateProvinceCommandTestContext
    {
        public UpdateProvinceCommandTestContext()
        {
            ProvinceRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Province>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<Province>()).Returns(ProvinceRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Country>()).Returns(CountryRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Region>()).Returns(RegionRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Province>> ProvinceRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Country>> CountryRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Region>> RegionRepositoryMock { get; } = new();

        public UpdateProvinceCommandHandler CreateHandler()
        {
            return new UpdateProvinceCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
