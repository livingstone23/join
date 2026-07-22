using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.CompanyModules.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.CompanyModules.Commands.UpdateCompanyModules;

/// <summary>
/// Contains the unit tests for the company-module update flow.
/// </summary>
public sealed class UpdateCompanyModulesCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant context is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        var context = new UpdateCompanyModulesCommandTestContext();
        var handler = context.CreateHandler();

        var response = await handler.Handle(CreateValidCommand(Guid.NewGuid(), Guid.Empty) with { CompanyId = Guid.Empty }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("CompanyId is required.");

        context.CompanyModuleRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CompanyModule>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the validation branch when the selected company does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnInvalidCompanyIdError()
    {
        var context = new UpdateCompanyModulesCommandTestContext();
        var request = CreateValidCommand(_fixture.Create<Guid>(), _fixture.Create<Guid>());
        context.CompanyRepositoryMock.Setup(x => x.GetAsync(request.CompanyId)).ReturnsAsync((Company?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The specified CompanyId does not exist.");
    }

    /// <summary>
    /// Verifies the not-found branch when the assignment cannot be resolved for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAssignmentDoesNotExist_ShouldReturnNotFoundError()
    {
        var context = new UpdateCompanyModulesCommandTestContext();
        var request = CreateValidCommand(_fixture.Create<Guid>(), _fixture.Create<Guid>());

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(request.CompanyId))
            .ReturnsAsync(new Company { Name = "JOIN" });

        context.CompanyModuleRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<CompanyModule>());

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_MODULE_NOT_FOUND");
        response.Errors.Should().Contain("Company module not found.");

        context.CompanyModuleRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<CompanyModule>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence-failure branch when the commit affects no rows.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        var context = new UpdateCompanyModulesCommandTestContext();
        var companyId = _fixture.Create<Guid>();
        var entity = new CompanyModule
        {
            CompanyId = companyId,
            ModuleId = _fixture.Create<Guid>(),
            IsActive = false,
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id, companyId) with { IsActive = true };

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(request.CompanyId))
            .ReturnsAsync(new Company { Name = "JOIN" });

        context.CompanyModuleRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { entity });

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UPDATE_FAILED");
        response.Errors.Should().Contain("No records were affected while updating the company module assignment.");
        entity.IsActive.Should().BeTrue();

        context.CompanyModuleRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    /// <summary>
    /// Verifies the successful update flow and DTO projection.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateAssignmentAndReturnDto()
    {
        var context = new UpdateCompanyModulesCommandTestContext();
        var companyId = _fixture.Create<Guid>();
        var moduleId = _fixture.Create<Guid>();
        var entity = new CompanyModule
        {
            CompanyId = companyId,
            ModuleId = moduleId,
            IsActive = false,
            GcRecord = 0
        };

        var request = CreateValidCommand(entity.Id, companyId) with { IsActive = true };

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(request.CompanyId))
            .ReturnsAsync(new Company { Name = "JOIN CRM" });

        context.CompanyModuleRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { entity });

        context.ModuleRepositoryMock
            .Setup(x => x.GetAsync(moduleId))
            .ReturnsAsync(new SystemModule { Name = "Messaging" });

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Company module updated successfully.");
        entity.IsActive.Should().BeTrue();

        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(entity.Id);
        response.Data.CompanyId.Should().Be(companyId);
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.ModuleId.Should().Be(moduleId);
        response.Data.ModuleName.Should().Be("Messaging");
        response.Data.IsActive.Should().BeTrue();
        response.Data.CreatedAt.Should().Be(entity.Created);

        context.CompanyModuleRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid update command.
    /// </summary>
    private static UpdateCompanyModulesCommand CreateValidCommand(Guid id, Guid companyId)
    {
        return new UpdateCompanyModulesCommand
        {
            Id = id,
            CompanyId = companyId,
            IsActive = false
        };
    }

    /// <summary>
    /// Holds the reusable mocks for the update handler tests.
    /// </summary>
    private sealed class UpdateCompanyModulesCommandTestContext
    {
        public UpdateCompanyModulesCommandTestContext()
        {
            CompanyModuleRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<CompanyModule>());
            CompanyModuleRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<CompanyModule>())).ReturnsAsync(true);

            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(CompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CompanyModule>()).Returns(CompanyModuleRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<SystemModule>()).Returns(ModuleRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CompanyModule>> CompanyModuleRepositoryMock { get; } = new();
        public Mock<IGenericRepository<SystemModule>> ModuleRepositoryMock { get; } = new();

        public UpdateCompanyModulesCommandHandler CreateHandler()
        {
            return new UpdateCompanyModulesCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
