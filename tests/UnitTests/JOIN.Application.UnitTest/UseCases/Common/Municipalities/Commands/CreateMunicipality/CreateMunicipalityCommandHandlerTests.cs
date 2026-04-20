using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Municipalities.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Municipalities.Commands.CreateMunicipality;

/// <summary>
/// Contains the unit tests for the municipality creation command.
/// These tests verify foreign-key validation, duplicate protections, and normalization.
/// </summary>
public sealed class CreateMunicipalityCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the validation branch when the requested province does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProvinceDoesNotExist_ShouldReturnProvinceNotFoundError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var context = new CreateMunicipalityCommandTestContext();
        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync((Province?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(provinceId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MUNICIPALITY_PROVINCE_NOT_FOUND");
        response.Errors.Should().Contain("The specified ProvinceId does not exist.");
    }

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison within the same province.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ShouldReturnNameInUseError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var context = new CreateMunicipalityCommandTestContext();
        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(CreateProvince(provinceId));
        context.MunicipalityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            new Municipality { Name = "Managua", Code = "MG", ProvinceId = provinceId, GcRecord = 0 }
        });
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(provinceId) with { Name = "  managua  " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MUNICIPALITY_NAME_IN_USE");
        response.Errors.Should().Contain("Another active municipality already uses the same name for this province.");
    }

    /// <summary>
    /// Verifies the duplicate-code branch using a case-insensitive comparison within the same province.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeAlreadyExists_ShouldReturnCodeInUseError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var context = new CreateMunicipalityCommandTestContext();
        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(CreateProvince(provinceId));
        context.MunicipalityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            new Municipality { Name = "León", Code = "MN", ProvinceId = provinceId, GcRecord = 0 }
        });
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(provinceId) with { Code = " mn " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MUNICIPALITY_CODE_IN_USE");
        response.Errors.Should().Contain("Another active municipality already uses the same code for this province.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the save operation affects no rows.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var context = new CreateMunicipalityCommandTestContext();
        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(CreateProvince(provinceId));
        context.MunicipalityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Municipality>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(provinceId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
        response.Errors.Should().Contain("No records were affected while creating the municipality.");
    }

    /// <summary>
    /// Verifies the successful creation flow and code normalization.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateMunicipalityAndReturnDto()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var context = new CreateMunicipalityCommandTestContext();
        Municipality? insertedEntity = null;

        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(CreateProvince(provinceId));
        context.MunicipalityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Municipality>());
        context.MunicipalityRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<Municipality>()))
            .Callback<Municipality>(entity => insertedEntity = entity)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(provinceId) with { Name = "  Managua  ", Code = " mn " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Municipality created successfully.");
        insertedEntity.Should().NotBeNull();
        insertedEntity!.Name.Should().Be("Managua");
        insertedEntity.Code.Should().Be("MN");
        insertedEntity.ProvinceId.Should().Be(provinceId);
        response.Data.Should().NotBeNull();
        response.Data!.ProvinceName.Should().Be("Managua Province");
    }

    /// <summary>
    /// Creates a valid command for municipality creation scenarios.
    /// </summary>
    private static CreateMunicipalityCommand CreateValidCommand(Guid provinceId)
    {
        return new CreateMunicipalityCommand
        {
            Name = "Managua",
            Code = "MN",
            ProvinceId = provinceId
        };
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
    /// Holds the reusable mocks and helper factory for the create handler.
    /// </summary>
    private sealed class CreateMunicipalityCommandTestContext
    {
        public CreateMunicipalityCommandTestContext()
        {
            MunicipalityRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<Municipality>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<Municipality>()).Returns(MunicipalityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Province>()).Returns(ProvinceRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Municipality>> MunicipalityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Province>> ProvinceRepositoryMock { get; } = new();

        public CreateMunicipalityCommandHandler CreateHandler()
        {
            return new CreateMunicipalityCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
