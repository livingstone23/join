using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Projects.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Projects.Commands.DeleteProject;

/// <summary>
/// Contains the unit tests for the logical delete flow of projects.
/// These tests verify tenant protection, not-found behavior, ticket dependency guards,
/// persistence failures, and the successful soft-delete path.
/// </summary>
public sealed class DeleteProjectCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant company identifier is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new DeleteProjectCommandTestContext();
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteProjectCommand(_fixture.Create<Guid>(), Guid.Empty), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The X-Company-Id header is required.");

        context.UnitOfWorkMock.Verify(x => x.GetRepository<Project>(), Times.Never);
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
        var context = new DeleteProjectCommandTestContext();

        context.ProjectRepositoryMock
            .Setup(x => x.GetAsync(projectId))
            .ReturnsAsync((Project?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteProjectCommand(projectId, companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROJECT_NOT_FOUND");
        response.Errors.Should().Contain("Project not found.");
        context.ProjectRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Project>()), Times.Never);
    }

    /// <summary>
    /// Verifies the guard branch when the project is referenced by active tickets.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProjectIsInUse_ShouldReturnProjectInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var project = new Project
        {
            CompanyId = companyId,
            Name = "Portal Migration",
            EntityStatusId = _fixture.Create<Guid>(),
            GcRecord = 0
        };

        var context = new DeleteProjectCommandTestContext();
        context.ProjectRepositoryMock
            .Setup(x => x.GetAsync(project.Id))
            .ReturnsAsync(project);

        context.TicketRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new Ticket
                {
                    CompanyId = companyId,
                    ProjectId = project.Id,
                    GcRecord = 0
                }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteProjectCommand(project.Id, companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PROJECT_IN_USE");
        response.Errors.Should().Contain("The project is currently assigned to one or more tickets and cannot be deleted.");
        context.ProjectRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Project>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var project = new Project
        {
            CompanyId = companyId,
            Name = "Portal Migration",
            EntityStatusId = _fixture.Create<Guid>(),
            GcRecord = 0
        };

        var context = new DeleteProjectCommandTestContext();
        context.ProjectRepositoryMock
            .Setup(x => x.GetAsync(project.Id))
            .ReturnsAsync(project);

        context.TicketRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Ticket>());

        context.ProjectRepositoryMock
            .Setup(x => x.UpdateAsync(project))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteProjectCommand(project.Id, companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the project.");
    }

    /// <summary>
    /// Verifies the happy path for a logical delete operation.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProjectCanBeDeleted_ShouldMarkEntityAsDeletedAndReturnSuccess()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var project = new Project
        {
            CompanyId = companyId,
            Name = "Portal Migration",
            EntityStatusId = _fixture.Create<Guid>(),
            GcRecord = 0
        };

        var context = new DeleteProjectCommandTestContext();
        context.ProjectRepositoryMock
            .Setup(x => x.GetAsync(project.Id))
            .ReturnsAsync(project);

        context.TicketRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<Ticket>());

        context.ProjectRepositoryMock
            .Setup(x => x.UpdateAsync(project))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteProjectCommand(project.Id, companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Project deleted successfully.");
        response.Data.Should().Be(project.Id);
        project.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);

        context.ProjectRepositoryMock.Verify(x => x.UpdateAsync(project), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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
    /// Holds the reusable mocks and helper factory for the delete handler.
    /// </summary>
    private sealed class DeleteProjectCommandTestContext
    {
        public DeleteProjectCommandTestContext()
        {
            SetupRepository(UnitOfWorkMock, ProjectRepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Project>> ProjectRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = new();

        public DeleteProjectCommandHandler CreateHandler()
        {
            return new DeleteProjectCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
