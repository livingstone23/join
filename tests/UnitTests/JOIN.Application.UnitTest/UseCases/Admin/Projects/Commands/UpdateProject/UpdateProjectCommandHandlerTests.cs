using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Projects.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Projects.Commands.UpdateProject;

/// <summary>
/// Contains the unit tests for the project update command.
/// These tests verify tenant validation, existence guards, duplicate checks,
/// persistence failures, and the successful update flow.
/// </summary>
public sealed class UpdateProjectCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant company identifier is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new UpdateProjectCommandTestContext();
        var request = CreateValidCommand(_fixture.Create<Guid>(), Guid.Empty, _fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The X-Company-Id header is required.");

        context.UnitOfWorkMock.Verify(x => x.GetRepository<Company>(), Times.Never);
        context.ProjectRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Project>()), Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when the project does not exist for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProjectDoesNotExist_ShouldReturnProjectNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var projectId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var request = CreateValidCommand(projectId, companyId, statusId);
        var context = new UpdateProjectCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.ProjectRepositoryMock
            .Setup(x => x.GetAsync(projectId))
            .ReturnsAsync((Project?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROJECT_NOT_FOUND");
        response.Errors.Should().Contain("Project not found.");
        context.ProjectRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Project>()), Times.Never);
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
        var project = new Project
        {
            CompanyId = companyId,
            Name = "Legacy",
            EntityStatusId = _fixture.Create<Guid>(),
            GcRecord = 0,
            Created = new DateTime(2026, 4, 18, 8, 0, 0, DateTimeKind.Utc)
        };

        var request = CreateValidCommand(project.Id, companyId, statusId);
        var context = new UpdateProjectCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.ProjectRepositoryMock
            .Setup(x => x.GetAsync(project.Id))
            .ReturnsAsync(project);

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
        context.ProjectRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Project>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate-name branch using a case-insensitive comparison.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherProjectUsesSameName_ShouldReturnProjectNameInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var project = new Project
        {
            CompanyId = companyId,
            Name = "Legacy",
            EntityStatusId = statusId,
            GcRecord = 0
        };

        var request = CreateValidCommand(project.Id, companyId, statusId);
        var context = new UpdateProjectCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.ProjectRepositoryMock
            .Setup(x => x.GetAsync(project.Id))
            .ReturnsAsync(project);

        context.EntityStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync(new EntityStatus { Name = "Active" });

        context.ProjectRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                project,
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
        context.ProjectRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Project>()), Times.Never);
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
        var project = new Project
        {
            CompanyId = companyId,
            Name = "Legacy",
            EntityStatusId = _fixture.Create<Guid>(),
            GcRecord = 0
        };

        var request = CreateValidCommand(project.Id, companyId, statusId);
        var context = new UpdateProjectCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.ProjectRepositoryMock
            .Setup(x => x.GetAsync(project.Id))
            .ReturnsAsync(project);

        context.EntityStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync(new EntityStatus { Name = "Active" });

        context.ProjectRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync([project]);

        context.ProjectRepositoryMock
            .Setup(x => x.UpdateAsync(project))
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
        response.Errors.Should().Contain("No records were affected while updating the project.");
    }

    /// <summary>
    /// Verifies the happy path when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateProjectAndReturnDto()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var project = new Project
        {
            CompanyId = companyId,
            Name = "Legacy",
            EntityStatusId = _fixture.Create<Guid>(),
            GcRecord = 0
        };

        var request = CreateValidCommand(project.Id, companyId, statusId);
        var context = new UpdateProjectCommandTestContext();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        context.ProjectRepositoryMock
            .Setup(x => x.GetAsync(project.Id))
            .ReturnsAsync(project);

        context.EntityStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync(new EntityStatus { Name = "Active" });

        context.ProjectRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync([project]);

        context.ProjectRepositoryMock
            .Setup(x => x.UpdateAsync(project))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Project updated successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(project.Id);
        response.Data.CompanyId.Should().Be(companyId);
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.Name.Should().Be("Portal Migration");
        response.Data.EntityStatusId.Should().Be(statusId);
        response.Data.EntityStatusName.Should().Be("Active");
        response.Data.CreatedAt.Should().Be(project.Created);

        project.CompanyId.Should().Be(companyId);
        project.Name.Should().Be("Portal Migration");
        project.EntityStatusId.Should().Be(statusId);
        context.ProjectRepositoryMock.Verify(x => x.UpdateAsync(project), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid command instance for the project update flow.
    /// </summary>
    private static UpdateProjectCommand CreateValidCommand(Guid id, Guid companyId, Guid statusId)
    {
        return new UpdateProjectCommand
        {
            Id = id,
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
    /// Holds the reusable mocks and helper factory for the update handler.
    /// </summary>
    private sealed class UpdateProjectCommandTestContext
    {
        public UpdateProjectCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, EntityStatusRepositoryMock);
            SetupRepository(UnitOfWorkMock, ProjectRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<EntityStatus>> EntityStatusRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Project>> ProjectRepositoryMock { get; } = new();

        public UpdateProjectCommandHandler CreateHandler()
        {
            return new UpdateProjectCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
