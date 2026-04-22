using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Areas.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Areas.Commands.UpdateArea;

/// <summary>
/// Contains the unit tests for the area update command.
/// These tests verify tenant validation, existence guards, duplicate checks,
/// persistence failures, and the successful update flow.
/// </summary>
public sealed class UpdateAreaCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant company identifier is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new UpdateAreaCommandTestContext();
        var request = CreateValidCommand(_fixture.Create<Guid>(), Guid.Empty, _fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The X-Company-Id header is required.");

        context.UnitOfWorkMock.Verify(x => x.GetRepository<Company>(), Times.Never);
        context.AreaRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Area>()), Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when the area does not exist for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAreaDoesNotExist_ShouldReturnAreaNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var areaId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var request = CreateValidCommand(areaId, companyId, statusId);
        var context = new UpdateAreaCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.AreaRepositoryMock
            .Setup(x => x.GetAsync(areaId))
            .ReturnsAsync((Area?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("AREA_NOT_FOUND");
        response.Errors.Should().Contain("Area not found.");
        context.AreaRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Area>()), Times.Never);
    }

    /// <summary>
    /// Verifies the validation branch when the requested entity status does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityStatusDoesNotExist_ShouldReturnAreaStatusNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var area = new Area
        {
            CompanyId = companyId,
            Name = "Legacy",
            EntityStatusId = _fixture.Create<Guid>(),
            GcRecord = 0,
            Created = new DateTime(2026, 4, 18, 9, 30, 0, DateTimeKind.Utc)
        };

        var request = CreateValidCommand(area.Id, companyId, statusId);
        var context = new UpdateAreaCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.AreaRepositoryMock
            .Setup(x => x.GetAsync(area.Id))
            .ReturnsAsync(area);

        context.EntityStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync((EntityStatus?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("AREA_STATUS_NOT_FOUND");
        response.Errors.Should().Contain("The specified EntityStatusId does not exist.");
        context.AreaRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Area>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherAreaUsesSameName_ShouldReturnAreaNameInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var area = new Area
        {
            CompanyId = companyId,
            Name = "Legacy",
            EntityStatusId = statusId,
            GcRecord = 0
        };

        var request = CreateValidCommand(area.Id, companyId, statusId);
        var context = new UpdateAreaCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.AreaRepositoryMock
            .Setup(x => x.GetAsync(area.Id))
            .ReturnsAsync(area);

        context.EntityStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync(new EntityStatus { Name = "Active" });

        context.AreaRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                area,
                new Area
                {
                    CompanyId = companyId,
                    Name = request.Name.Trim().ToUpperInvariant(),
                    EntityStatusId = _fixture.Create<Guid>(),
                    GcRecord = 0
                }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("AREA_NAME_IN_USE");
        response.Errors.Should().Contain("Another active area already uses the same name in this company.");
        context.AreaRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Area>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var area = new Area
        {
            CompanyId = companyId,
            Name = "Legacy",
            EntityStatusId = _fixture.Create<Guid>(),
            GcRecord = 0
        };

        var request = CreateValidCommand(area.Id, companyId, statusId);
        var context = new UpdateAreaCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.AreaRepositoryMock
            .Setup(x => x.GetAsync(area.Id))
            .ReturnsAsync(area);

        context.EntityStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync(new EntityStatus { Name = "Active" });

        context.AreaRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync([area]);

        context.AreaRepositoryMock
            .Setup(x => x.UpdateAsync(area))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UPDATE_FAILED");
        response.Errors.Should().Contain("No records were affected while updating the area.");
    }

    /// <summary>
    /// Verifies the happy path when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateAreaAndReturnDto()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var area = new Area
        {
            CompanyId = companyId,
            Name = "Legacy",
            EntityStatusId = _fixture.Create<Guid>(),
            GcRecord = 0
        };

        var request = CreateValidCommand(area.Id, companyId, statusId);
        var context = new UpdateAreaCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.AreaRepositoryMock
            .Setup(x => x.GetAsync(area.Id))
            .ReturnsAsync(area);

        context.EntityStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync(new EntityStatus { Name = "Active" });

        context.AreaRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync([area]);

        context.AreaRepositoryMock
            .Setup(x => x.UpdateAsync(area))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Area updated successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(area.Id);
        response.Data.CompanyId.Should().Be(companyId);
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.Name.Should().Be("Support");
        response.Data.EntityStatusId.Should().Be(statusId);
        response.Data.EntityStatusName.Should().Be("Active");
        response.Data.Created.Should().Be(area.Created);

        area.CompanyId.Should().Be(companyId);
        area.Name.Should().Be("Support");
        area.EntityStatusId.Should().Be(statusId);
        context.AreaRepositoryMock.Verify(x => x.UpdateAsync(area), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid command instance for the area update flow.
    /// </summary>
    private static UpdateAreaCommand CreateValidCommand(Guid id, Guid companyId, Guid statusId)
    {
        return new UpdateAreaCommand
        {
            Id = id,
            CompanyId = companyId,
            Name = "  Support  ",
            EntityStatusId = statusId
        };
    }

    /// <summary>
    /// Registers a repository in the mocked unit of work using the generic resolution pattern.
    /// </summary>
    private static void SetupRepository<TEntity>(
        Mock<IUnitOfWork> unitOfWorkMock,
        Mock<IGenericRepository<TEntity>> repositoryMock)
        where TEntity : class
    {
        unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the update handler.
    /// </summary>
    private sealed class UpdateAreaCommandTestContext
    {
        public UpdateAreaCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, EntityStatusRepositoryMock);
            SetupRepository(UnitOfWorkMock, AreaRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<EntityStatus>> EntityStatusRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Area>> AreaRepositoryMock { get; } = new();

        public UpdateAreaCommandHandler CreateHandler()
        {
            return new UpdateAreaCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
