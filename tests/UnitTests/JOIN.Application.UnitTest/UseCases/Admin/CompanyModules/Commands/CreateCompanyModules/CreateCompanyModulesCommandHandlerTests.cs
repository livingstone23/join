using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.CompanyModules.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.CompanyModules.Commands.CreateCompanyModules;

/// <summary>
/// Contains the unit tests for the company-module creation flow.
/// </summary>
public sealed class CreateCompanyModulesCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant context is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        var context = new CreateCompanyModulesCommandTestContext();
        var handler = context.CreateHandler();

        var response = await handler.Handle(CreateValidCommand() with { CompanyId = Guid.Empty }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("CompanyId is required.");

        context.CompanyModuleRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CompanyModule>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the validation branch when the selected company does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnInvalidCompanyIdError()
    {
        var context = new CreateCompanyModulesCommandTestContext();
        var request = CreateValidCommand();
        context.CompanyRepositoryMock.Setup(x => x.GetAsync(request.CompanyId)).ReturnsAsync((Company?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The specified CompanyId does not exist.");

        context.CompanyModuleRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CompanyModule>()), Times.Never);
    }

    /// <summary>
    /// Verifies both unavailable-module branches: missing and soft-deleted modules.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenModuleIsUnavailable_ShouldReturnSystemModuleNotFoundError(bool isSoftDeleted)
    {
        var context = new CreateCompanyModulesCommandTestContext();
        var request = CreateValidCommand();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(request.CompanyId))
            .ReturnsAsync(new Company { Name = "JOIN" });

        var module = isSoftDeleted
            ? new SystemModule { Name = "Messaging", GcRecord = 20260420 }
            : null;

        context.ModuleRepositoryMock
            .Setup(x => x.GetAsync(request.ModuleId))
            .ReturnsAsync(module);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SYSTEM_MODULE_NOT_FOUND");
        response.Errors.Should().Contain("The specified ModuleId does not exist.");

        context.CompanyModuleRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CompanyModule>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate-assignment guard for an active company-module link.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAssignmentAlreadyExists_ShouldReturnDuplicateError()
    {
        var context = new CreateCompanyModulesCommandTestContext();
        var request = CreateValidCommand();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(request.CompanyId))
            .ReturnsAsync(new Company { Name = "JOIN" });

        context.ModuleRepositoryMock
            .Setup(x => x.GetAsync(request.ModuleId))
            .ReturnsAsync(new SystemModule { Name = "Messaging", GcRecord = 0 });

        context.CompanyModuleRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new CompanyModule
                {
                    CompanyId = request.CompanyId,
                    ModuleId = request.ModuleId,
                    GcRecord = 0,
                    IsActive = true
                }
            });

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_MODULE_ALREADY_EXISTS");
        response.Errors.Should().Contain("The selected module is already assigned to this company.");

        context.CompanyModuleRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CompanyModule>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence-failure branch when the insert succeeds but the commit affects no rows.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        var context = new CreateCompanyModulesCommandTestContext();
        var request = CreateValidCommand();
        CompanyModule? insertedEntity = null;

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(request.CompanyId))
            .ReturnsAsync(new Company { Name = "JOIN" });

        context.ModuleRepositoryMock
            .Setup(x => x.GetAsync(request.ModuleId))
            .ReturnsAsync(new SystemModule { Name = "Messaging", GcRecord = 0 });

        context.CompanyModuleRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CompanyModule>()))
            .Callback<CompanyModule>(entity => insertedEntity = entity)
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
        response.Errors.Should().Contain("No records were affected while creating the company module assignment.");
        insertedEntity.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies the successful creation flow and DTO projection.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateAssignmentAndReturnDto()
    {
        var context = new CreateCompanyModulesCommandTestContext();
        var request = CreateValidCommand();
        CompanyModule? insertedEntity = null;

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(request.CompanyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM" });

        context.ModuleRepositoryMock
            .Setup(x => x.GetAsync(request.ModuleId))
            .ReturnsAsync(new SystemModule { Name = "Messaging", GcRecord = 0 });

        context.CompanyModuleRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<CompanyModule>()))
            .Callback<CompanyModule>(entity => insertedEntity = entity)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Company module created successfully.");
        insertedEntity.Should().NotBeNull();
        insertedEntity!.CompanyId.Should().Be(request.CompanyId);
        insertedEntity.ModuleId.Should().Be(request.ModuleId);
        insertedEntity.IsActive.Should().BeTrue();

        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(insertedEntity.Id);
        response.Data.CompanyId.Should().Be(request.CompanyId);
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.ModuleId.Should().Be(request.ModuleId);
        response.Data.ModuleName.Should().Be("Messaging");
        response.Data.IsActive.Should().BeTrue();
        response.Data.CreatedAt.Should().Be(insertedEntity.Created);

        context.CompanyModuleRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<CompanyModule>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid command for reuse across test scenarios.
    /// </summary>
    private CreateCompanyModulesCommand CreateValidCommand()
    {
        return new CreateCompanyModulesCommand
        {
            CompanyId = _fixture.Create<Guid>(),
            ModuleId = _fixture.Create<Guid>(),
            IsActive = true
        };
    }

    /// <summary>
    /// Holds the reusable mocks for the create handler tests.
    /// </summary>
    private sealed class CreateCompanyModulesCommandTestContext
    {
        public CreateCompanyModulesCommandTestContext()
        {
            CompanyModuleRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<CompanyModule>());
            CompanyModuleRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<CompanyModule>())).ReturnsAsync(true);

            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(CompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<SystemModule>()).Returns(ModuleRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CompanyModule>()).Returns(CompanyModuleRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<SystemModule>> ModuleRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CompanyModule>> CompanyModuleRepositoryMock { get; } = new();

        public CreateCompanyModulesCommandHandler CreateHandler()
        {
            return new CreateCompanyModulesCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
