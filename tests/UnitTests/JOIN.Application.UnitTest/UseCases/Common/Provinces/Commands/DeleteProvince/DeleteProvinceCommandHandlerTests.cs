using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Provinces.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Provinces.Commands.DeleteProvince;

/// <summary>
/// Contains the unit tests for the province delete command.
/// These tests verify dependency protection and the soft-delete behavior.
/// </summary>
public sealed class DeleteProvinceCommandHandlerTests
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
        var context = new DeleteProvinceCommandTestContext();
        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync((Province?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteProvinceCommand(provinceId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_NOT_FOUND");
        response.Errors.Should().Contain("Province not found.");
    }

    /// <summary>
    /// Verifies the protected branch when active municipalities reference the province.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProvinceIsUsedByMunicipalities_ShouldReturnInUseError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var context = new DeleteProvinceCommandTestContext();
        var entity = CreateProvince(provinceId);

        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(entity);
        context.MunicipalityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            new Municipality { Name = "District 1", ProvinceId = provinceId, GcRecord = 0 }
        });
        context.CustomerAddressRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<CustomerAddress>());

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteProvinceCommand(provinceId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_IN_USE");
        response.Errors.Should().Contain("The province is currently linked to municipalities or customer addresses and cannot be deleted.");
    }

    /// <summary>
    /// Verifies the protected branch when active customer addresses reference the province.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProvinceIsUsedByCustomerAddresses_ShouldReturnInUseError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var context = new DeleteProvinceCommandTestContext();
        var entity = CreateProvince(provinceId);

        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(entity);
        context.MunicipalityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Municipality>());
        context.CustomerAddressRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            new CustomerAddress
            {
                CompanyId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                AddressLine1 = "Main street",
                ZipCode = "12001",
                StreetTypeId = Guid.NewGuid(),
                CountryId = Guid.NewGuid(),
                ProvinceId = provinceId,
                MunicipalityId = Guid.NewGuid(),
                IsDefault = true,
                GcRecord = 0
            }
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteProvinceCommand(provinceId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROVINCE_IN_USE");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the soft delete is not committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var context = new DeleteProvinceCommandTestContext();
        var entity = CreateProvince(provinceId);

        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(entity);
        context.MunicipalityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Municipality>());
        context.CustomerAddressRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<CustomerAddress>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteProvinceCommand(provinceId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the province.");
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
    }

    /// <summary>
    /// Verifies the happy path when the province can be soft deleted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProvinceIsNotInUse_ShouldSoftDeleteAndReturnSuccess()
    {
        // Arrange
        var provinceId = _fixture.Create<Guid>();
        var context = new DeleteProvinceCommandTestContext();
        var entity = CreateProvince(provinceId);

        context.ProvinceRepositoryMock.Setup(x => x.GetAsync(provinceId)).ReturnsAsync(entity);
        context.MunicipalityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Municipality>());
        context.CustomerAddressRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<CustomerAddress>());

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteProvinceCommand(provinceId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Province deleted successfully.");
        response.Data.Should().Be(provinceId);
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
        context.ProvinceRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    /// <summary>
    /// Creates a province entity for delete scenarios.
    /// </summary>
    private static Province CreateProvince(Guid provinceId)
    {
        var entity = new Province
        {
            Name = "Managua",
            Code = "MN",
            CountryId = Guid.NewGuid(),
            RegionId = null
        };

        typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(entity, provinceId);
        return entity;
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the delete handler.
    /// </summary>
    private sealed class DeleteProvinceCommandTestContext
    {
        public DeleteProvinceCommandTestContext()
        {
            ProvinceRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Province>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<Province>()).Returns(ProvinceRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Municipality>()).Returns(MunicipalityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CustomerAddress>()).Returns(CustomerAddressRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Province>> ProvinceRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Municipality>> MunicipalityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CustomerAddress>> CustomerAddressRepositoryMock { get; } = new();

        public DeleteProvinceCommandHandler CreateHandler()
        {
            return new DeleteProvinceCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
