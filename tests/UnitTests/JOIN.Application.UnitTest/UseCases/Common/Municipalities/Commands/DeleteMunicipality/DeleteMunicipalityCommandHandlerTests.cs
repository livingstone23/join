using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Municipalities.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Municipalities.Commands.DeleteMunicipality;

/// <summary>
/// Contains the unit tests for the municipality delete command.
/// These tests verify dependency protection and soft-delete behavior.
/// </summary>
public sealed class DeleteMunicipalityCommandHandlerTests
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
        var context = new DeleteMunicipalityCommandTestContext();
        context.MunicipalityRepositoryMock.Setup(x => x.GetAsync(municipalityId)).ReturnsAsync((Municipality?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteMunicipalityCommand(municipalityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MUNICIPALITY_NOT_FOUND");
        response.Errors.Should().Contain("Municipality not found.");
    }

    /// <summary>
    /// Verifies the protected branch when active customer addresses reference the municipality.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMunicipalityIsUsedByCustomerAddresses_ShouldReturnInUseError()
    {
        // Arrange
        var municipalityId = _fixture.Create<Guid>();
        var context = new DeleteMunicipalityCommandTestContext();
        var entity = CreateMunicipality(municipalityId);

        context.MunicipalityRepositoryMock.Setup(x => x.GetAsync(municipalityId)).ReturnsAsync(entity);
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
                ProvinceId = Guid.NewGuid(),
                MunicipalityId = municipalityId,
                IsDefault = true,
                GcRecord = 0
            }
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteMunicipalityCommand(municipalityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MUNICIPALITY_IN_USE");
        response.Errors.Should().Contain("The municipality is currently linked to customer addresses and cannot be deleted.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the soft delete is not committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var municipalityId = _fixture.Create<Guid>();
        var context = new DeleteMunicipalityCommandTestContext();
        var entity = CreateMunicipality(municipalityId);

        context.MunicipalityRepositoryMock.Setup(x => x.GetAsync(municipalityId)).ReturnsAsync(entity);
        context.CustomerAddressRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<CustomerAddress>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteMunicipalityCommand(municipalityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the municipality.");
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
    }

    /// <summary>
    /// Verifies the happy path when the municipality can be soft deleted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMunicipalityIsNotInUse_ShouldSoftDeleteAndReturnSuccess()
    {
        // Arrange
        var municipalityId = _fixture.Create<Guid>();
        var context = new DeleteMunicipalityCommandTestContext();
        var entity = CreateMunicipality(municipalityId);

        context.MunicipalityRepositoryMock.Setup(x => x.GetAsync(municipalityId)).ReturnsAsync(entity);
        context.CustomerAddressRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<CustomerAddress>());

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteMunicipalityCommand(municipalityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Municipality deleted successfully.");
        response.Data.Should().Be(municipalityId);
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
        context.MunicipalityRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    /// <summary>
    /// Creates a municipality entity for delete scenarios.
    /// </summary>
    private static Municipality CreateMunicipality(Guid municipalityId)
    {
        var entity = new Municipality
        {
            Name = "Managua",
            Code = "MN",
            ProvinceId = Guid.NewGuid()
        };

        typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(entity, municipalityId);
        return entity;
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the delete handler.
    /// </summary>
    private sealed class DeleteMunicipalityCommandTestContext
    {
        public DeleteMunicipalityCommandTestContext()
        {
            MunicipalityRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Municipality>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<Municipality>()).Returns(MunicipalityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CustomerAddress>()).Returns(CustomerAddressRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Municipality>> MunicipalityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CustomerAddress>> CustomerAddressRepositoryMock { get; } = new();

        public DeleteMunicipalityCommandHandler CreateHandler()
        {
            return new DeleteMunicipalityCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
