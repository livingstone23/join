using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Countries.Commands;
using JOIN.Domain.Common;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Countries.Commands.DeleteCountry;

/// <summary>
/// Contains the unit tests for the country soft-delete command handler.
/// These tests verify not-found protection, persistence failures, and the successful delete flow.
/// </summary>
public sealed class DeleteCountryCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the requested country does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCountryDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var countryId = _fixture.Create<Guid>();
        var request = new DeleteCountryCommand(countryId);
        var context = new DeleteCountryCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(countryId))
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
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var entity = new Country
        {
            Name = "Panama",
            IsoCode = "PA",
            GcRecord = 0
        };

        var request = new DeleteCountryCommand(entity.Id);
        var context = new DeleteCountryCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

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
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the country.");
    }

    /// <summary>
    /// Verifies the happy path when the country is successfully soft-deleted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldSoftDeleteCountryAndReturnId()
    {
        // Arrange
        var entity = new Country
        {
            Name = "Panama",
            IsoCode = "PA",
            GcRecord = 0
        };

        var request = new DeleteCountryCommand(entity.Id);
        var context = new DeleteCountryCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

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
        response.Message.Should().Be("Country deleted successfully.");
        response.Data.Should().Be(entity.Id);

        entity.GcRecord.Should().NotBe(0);

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// SPEC 41: every active child type blocks the delete and is listed in the errors.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActiveDependentsExist_ShouldReturnInUseWithDetails()
    {
        // Arrange
        var entity = new Country
        {
            Name = "Panama",
            IsoCode = "PA",
            GcRecord = 0
        };

        var request = new DeleteCountryCommand(entity.Id);
        var context = new DeleteCountryCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Arrange: one row per child type that blocks the delete (SPEC 41).
        var child1 = new Province { Name = "Managua", Code = "MN", CountryId = entity.Id };
        context.UnitOfWorkMock.SetupRepositoryRows<Province>([child1]);
        var child2 = new Region { CompanyId = Guid.NewGuid(), Name = "North", CountryId = entity.Id };
        context.UnitOfWorkMock.SetupRepositoryRows<Region>([child2]);
        var child3 = new PersonAddress { CompanyId = Guid.NewGuid(), PersonId = Guid.NewGuid(), CountryId = entity.Id };
        context.UnitOfWorkMock.SetupRepositoryRows<PersonAddress>([child3]);


        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COUNTRY_IN_USE");
        response.Errors.Should().Equal("Active provinces: 1", "Active regions: 1", "Active person addresses: 1");
        entity.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
    }

    /// <summary>
    /// SPEC 41: logically deleted children do not block the delete.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDependentsAreDeleted_ShouldSoftDelete()
    {
        // Arrange
        var entity = new Country
        {
            Name = "Panama",
            IsoCode = "PA",
            GcRecord = 0
        };

        var request = new DeleteCountryCommand(entity.Id);
        var context = new DeleteCountryCommandTestContext();

        context.RepositoryMock
            .Setup(x => x.GetAsync(entity.Id))
            .ReturnsAsync(entity);

        context.RepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Arrange: one row per child type that blocks the delete (SPEC 41).
        var child1 = new Province { Name = "Managua", Code = "MN", CountryId = entity.Id };
        child1.MarkAsDeleted();
        context.UnitOfWorkMock.SetupRepositoryRows<Province>([child1]);
        var child2 = new Region { CompanyId = Guid.NewGuid(), Name = "North", CountryId = entity.Id };
        child2.MarkAsDeleted();
        context.UnitOfWorkMock.SetupRepositoryRows<Region>([child2]);
        var child3 = new PersonAddress { CompanyId = Guid.NewGuid(), PersonId = Guid.NewGuid(), CountryId = entity.Id };
        child3.MarkAsDeleted();
        context.UnitOfWorkMock.SetupRepositoryRows<PersonAddress>([child3]);


        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Country deleted successfully.");
        response.Data.Should().Be(entity.Id);

        entity.GcRecord.Should().NotBe(0);

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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
    /// Holds the reusable mocks and helper factory for the delete handler.
    /// </summary>
    private sealed class DeleteCountryCommandTestContext
    {
        public DeleteCountryCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, RepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<IGenericRepository<Country>> RepositoryMock { get; } = new();

        public DeleteCountryCommandHandler CreateHandler()
        {
            return new DeleteCountryCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
