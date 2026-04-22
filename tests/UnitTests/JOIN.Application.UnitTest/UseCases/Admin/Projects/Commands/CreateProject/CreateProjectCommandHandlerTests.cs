using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Projects.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Projects.Commands.CreateProject;

/// <summary>
/// Contains the unit tests for the project creation command.
/// These tests verify tenant validation, company and status guards, duplicate checks,
/// persistence failures, and the successful creation flow.
/// </summary>
public sealed class CreateProjectCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant company identifier is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new CreateProjectCommandTestContext();
        var request = CreateValidCommand(Guid.Empty, _fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The X-Company-Id header is required.");

        context.UnitOfWorkMock.Verify(x => x.GetRepository<Company>(), Times.Never);
        context.ProjectRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Project>()), Times.Never);
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
        var context = new CreateProjectCommandTestContext();

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
        context.ProjectRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Project>()), Times.Never);
    }

    /// <summary>
    /// Verifies the validation branch when the requested entity status does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityStatusDoesNotExist_ShouldReturnProjectStatusNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var request = CreateValidCommand(companyId, statusId);
        var context = new CreateProjectCommandTestContext();

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
        response.Message.Should().Be("PROJECT_STATUS_NOT_FOUND");
        response.Errors.Should().Contain("The specified EntityStatusId does not exist.");
        context.ProjectRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Project>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProjectNameAlreadyExists_ShouldReturnProjectNameInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var request = CreateValidCommand(companyId, statusId);
        var context = new CreateProjectCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.EntityStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync(new EntityStatus { Name = "Active" });

        context.ProjectRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new Project
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
        response.Message.Should().Be("PROJECT_NAME_IN_USE");
        response.Errors.Should().Contain("Another active project already uses the same name in this company.");
        context.ProjectRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Project>()), Times.Never);
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
        var context = new CreateProjectCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.EntityStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync(new EntityStatus { Name = "Active" });

        context.ProjectRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Project>());

        context.ProjectRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<Project>()))
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
        response.Errors.Should().Contain("No records were affected while creating the project.");
    }

    /// <summary>
    /// Verifies the happy path when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateProjectAndReturnDto()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var request = CreateValidCommand(companyId, statusId);
        var context = new CreateProjectCommandTestContext();
        Project? insertedProject = null;

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.EntityStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync(new EntityStatus { Name = "Active" });

        context.ProjectRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Project>());

        context.ProjectRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<Project>()))
            .Callback<Project>(project => insertedProject = project)
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Project created successfully.");
        response.Data.Should().NotBeNull();
        insertedProject.Should().NotBeNull();
        response.Data!.Id.Should().Be(insertedProject!.Id);
        response.Data!.CompanyId.Should().Be(companyId);
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.Name.Should().Be("Portal Migration");
        response.Data.EntityStatusId.Should().Be(statusId);
        response.Data.EntityStatusName.Should().Be("Active");
        response.Data.CreatedAt.Should().Be(insertedProject.Created);

        context.ProjectRepositoryMock.Verify(x => x.InsertAsync(It.Is<Project>(project =>
            project.CompanyId == companyId
            && project.Name == "Portal Migration"
            && project.EntityStatusId == statusId)), Times.Once);

        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid command instance for the project creation flow.
    /// </summary>
    private static CreateProjectCommand CreateValidCommand(Guid companyId, Guid statusId)
    {
        return new CreateProjectCommand
        {
            CompanyId = companyId,
            Name = "  Portal Migration  ",
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
    private sealed class CreateProjectCommandTestContext
    {
        public CreateProjectCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, EntityStatusRepositoryMock);
            SetupRepository(UnitOfWorkMock, ProjectRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<EntityStatus>> EntityStatusRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Project>> ProjectRepositoryMock { get; } = new();

        public CreateProjectCommandHandler CreateHandler()
        {
            return new CreateProjectCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
