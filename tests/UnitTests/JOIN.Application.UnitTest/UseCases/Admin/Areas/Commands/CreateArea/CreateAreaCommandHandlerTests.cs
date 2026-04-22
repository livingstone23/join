using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Areas.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Areas.Commands.CreateArea;

/// <summary>
/// Contains the unit tests for the area creation command.
/// These tests verify tenant validation, company and status guards, duplicate checks,
/// persistence failures, and the successful creation flow.
/// </summary>
public sealed class CreateAreaCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant company identifier is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new CreateAreaCommandTestContext();
        var request = CreateValidCommand(Guid.Empty, _fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The X-Company-Id header is required.");

        context.UnitOfWorkMock.Verify(x => x.GetRepository<Company>(), Times.Never);
        context.AreaRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Area>()), Times.Never);
    }

    /// <summary>
    /// Verifies the validation branch when the requested company does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var request = CreateValidCommand(companyId, _fixture.Create<Guid>());
        var context = new CreateAreaCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync((Company?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The specified CompanyId does not exist.");
        context.AreaRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Area>()), Times.Never);
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
        var request = CreateValidCommand(companyId, statusId);
        var context = new CreateAreaCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

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
        context.AreaRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Area>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAreaNameAlreadyExists_ShouldReturnAreaNameInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var request = CreateValidCommand(companyId, statusId);
        var context = new CreateAreaCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.EntityStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync(new EntityStatus { Name = "Active" });

        context.AreaRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
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
        context.AreaRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Area>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var request = CreateValidCommand(companyId, statusId);
        var context = new CreateAreaCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.EntityStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync(new EntityStatus { Name = "Active" });

        context.AreaRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Area>());

        context.AreaRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<Area>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
        response.Errors.Should().Contain("No records were affected while creating the area.");
    }

    /// <summary>
    /// Verifies the happy path when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateAreaAndReturnDto()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var request = CreateValidCommand(companyId, statusId);
        var context = new CreateAreaCommandTestContext();
        Area? insertedArea = null;

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.EntityStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync(new EntityStatus { Name = "Active" });

        context.AreaRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Area>());

        context.AreaRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<Area>()))
            .Callback<Area>(area => insertedArea = area)
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Area created successfully.");
        response.Data.Should().NotBeNull();
        insertedArea.Should().NotBeNull();
        response.Data!.Id.Should().Be(insertedArea!.Id);
        response.Data!.CompanyId.Should().Be(companyId);
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.Name.Should().Be("Support");
        response.Data.EntityStatusId.Should().Be(statusId);
        response.Data.EntityStatusName.Should().Be("Active");
        response.Data.Created.Should().Be(insertedArea.Created);

        context.AreaRepositoryMock.Verify(x => x.InsertAsync(It.Is<Area>(area =>
            area.CompanyId == companyId
            && area.Name == "Support"
            && area.EntityStatusId == statusId)), Times.Once);

        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid command instance for the area creation flow.
    /// </summary>
    private static CreateAreaCommand CreateValidCommand(Guid companyId, Guid statusId)
    {
        return new CreateAreaCommand
        {
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
    /// Holds the reusable mocks and helper factory for the create handler.
    /// </summary>
    private sealed class CreateAreaCommandTestContext
    {
        public CreateAreaCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, EntityStatusRepositoryMock);
            SetupRepository(UnitOfWorkMock, AreaRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<EntityStatus>> EntityStatusRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Area>> AreaRepositoryMock { get; } = new();

        public CreateAreaCommandHandler CreateHandler()
        {
            return new CreateAreaCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
