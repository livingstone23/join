using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Municipalities.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Municipalities.Commands.UpdateMunicipality;

/// <summary>
/// Contains the unit tests for the municipality update command.
/// These tests verify not-found handling, province validation, and duplicate protection.
/// </summary>
public sealed class UpdateMunicipalityCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the municipality does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMunicipalityDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var municipalityId = _fixture.Create<Guid>();
        var provinceId = _fixture.Create<Guid>();
        var context = new UpdateMunicipalityCommandTestContext();
        context.MunicipalityRepositoryMock.Setup(x => x.GetAsync(municipalityId)).ReturnsAsync((Municipality?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(municipalityId, provinceId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MUNICIPALITY_NOT_FOUND");
        response.Errors.Should().Contain("Municipality not found.");
    }

    /// <summary>
    /// Verifies the validation branch when the requested province does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProvinceDoesNotExist_ShouldReturnProvinceNotFoundError()
    {
        // Arrange
        var municipalityId = _fixture.Create<Guid>();
        var provinceId = _fixture.Create<Guid>();
        var context = new UpdateMunicipalityCommandTestContext();
        var municipality = CreateMunicipality(municipalityId, provinceId);
        context.MunicipalityRepositoryMock.Setup(x => x.GetAsync(municipalityId)).ReturnsAsync(municipality);
        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync((Province?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(municipalityId, provinceId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MUNICIPALITY_PROVINCE_NOT_FOUND");
    }

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison within the same province.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherMunicipalityUsesSameName_ShouldReturnNameInUseError()
    {
        // Arrange
        var municipalityId = _fixture.Create<Guid>();
        var provinceId = _fixture.Create<Guid>();
        var context = new UpdateMunicipalityCommandTestContext();
        var municipality = CreateMunicipality(municipalityId, provinceId, name: "Old", code: "OL");

        context.MunicipalityRepositoryMock.Setup(x => x.GetAsync(municipalityId)).ReturnsAsync(municipality);
        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(CreateProvince(provinceId));
        context.MunicipalityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            municipality,
            CreateMunicipality(_fixture.Create<Guid>(), provinceId, name: "Managua", code: "MG")
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(municipalityId, provinceId) with { Name = " managua " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MUNICIPALITY_NAME_IN_USE");
    }

    /// <summary>
    /// Verifies the duplicate-code branch using a case-insensitive comparison within the same province.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherMunicipalityUsesSameCode_ShouldReturnCodeInUseError()
    {
        // Arrange
        var municipalityId = _fixture.Create<Guid>();
        var provinceId = _fixture.Create<Guid>();
        var context = new UpdateMunicipalityCommandTestContext();
        var municipality = CreateMunicipality(municipalityId, provinceId, name: "Old", code: "OL");

        context.MunicipalityRepositoryMock.Setup(x => x.GetAsync(municipalityId)).ReturnsAsync(municipality);
        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(CreateProvince(provinceId));
        context.MunicipalityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            municipality,
            CreateMunicipality(_fixture.Create<Guid>(), provinceId, name: "León", code: "MN")
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(municipalityId, provinceId) with { Code = " mn " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MUNICIPALITY_CODE_IN_USE");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the save operation affects no rows.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var municipalityId = _fixture.Create<Guid>();
        var provinceId = _fixture.Create<Guid>();
        var context = new UpdateMunicipalityCommandTestContext();
        var municipality = CreateMunicipality(municipalityId, provinceId, name: "Old", code: "OL");

        context.MunicipalityRepositoryMock.Setup(x => x.GetAsync(municipalityId)).ReturnsAsync(municipality);
        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(CreateProvince(provinceId));
        context.MunicipalityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { municipality });
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(municipalityId, provinceId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UPDATE_FAILED");
        response.Errors.Should().Contain("No records were affected while updating the municipality.");
    }

    /// <summary>
    /// Verifies the successful update flow and value normalization.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateMunicipalityAndReturnDto()
    {
        // Arrange
        var municipalityId = _fixture.Create<Guid>();
        var provinceId = _fixture.Create<Guid>();
        var context = new UpdateMunicipalityCommandTestContext();
        var municipality = CreateMunicipality(municipalityId, provinceId, name: "Old", code: "OL");

        context.MunicipalityRepositoryMock.Setup(x => x.GetAsync(municipalityId)).ReturnsAsync(municipality);
        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(CreateProvince(provinceId));
        context.MunicipalityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { municipality });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(municipalityId, provinceId) with { Name = "  Managua  ", Code = " mn " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Municipality updated successfully.");
        municipality.Name.Should().Be("Managua");
        municipality.Code.Should().Be("MN");
        response.Data.Should().NotBeNull();
        response.Data!.ProvinceName.Should().Be("Managua Province");
        context.MunicipalityRepositoryMock.Verify(x => x.UpdateAsync(municipality), Times.Once);
    }

    /// <summary>
    /// Creates a valid command for update scenarios.
    /// </summary>
    private static UpdateMunicipalityCommand CreateValidCommand(Guid municipalityId, Guid provinceId)
    {
        return new UpdateMunicipalityCommand
        {
            Id = municipalityId,
            Name = "Managua",
            Code = "MN",
            ProvinceId = provinceId
        };
    }

    /// <summary>
    /// Creates a municipality entity for update scenarios.
    /// </summary>
    private static Municipality CreateMunicipality(Guid municipalityId, Guid provinceId, string name = "León", string? code = "LE")
    {
        var entity = new Municipality
        {
            Name = name,
            Code = code,
            ProvinceId = provinceId
        };

        typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(entity, municipalityId);
        return entity;
    }

    /// <summary>
    /// Creates a province entity for foreign-key validation scenarios.
    /// </summary>
    private static Province CreateProvince(Guid provinceId)
    {
        var entity = new Province
        {
            Name = "Managua Province",
            Code = "MP",
            CountryId = Guid.NewGuid()
        };

        typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(entity, provinceId);
        return entity;
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the update handler.
    /// </summary>
    private sealed class UpdateMunicipalityCommandTestContext
    {
        public UpdateMunicipalityCommandTestContext()
        {
            MunicipalityRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Municipality>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<Municipality>()).Returns(MunicipalityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Province>()).Returns(ProvinceRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Municipality>> MunicipalityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Province>> ProvinceRepositoryMock { get; } = new();

        public UpdateMunicipalityCommandHandler CreateHandler()
        {
            return new UpdateMunicipalityCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
