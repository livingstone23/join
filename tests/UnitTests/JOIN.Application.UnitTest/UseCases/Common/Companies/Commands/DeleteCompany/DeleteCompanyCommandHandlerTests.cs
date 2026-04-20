using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Companies.Commands;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Companies.Commands.DeleteCompany;

/// <summary>
/// Contains the unit tests for the company delete command.
/// These tests verify not-found handling and soft-delete persistence.
/// </summary>
public sealed class DeleteCompanyCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the company does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteCompanyCommandTestContext();
        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync((Company?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteCompanyCommand(companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_NOT_FOUND");
        response.Errors.Should().Contain("Company not found.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the soft delete is not committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteCompanyCommandTestContext();
        var entity = CreateCompany(companyId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(entity);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteCompanyCommand(companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the company.");
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
    }

    /// <summary>
    /// Verifies the happy path when the company is soft deleted successfully.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyExists_ShouldSoftDeleteAndReturnSuccess()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteCompanyCommandTestContext();
        var entity = CreateCompany(companyId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(entity);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteCompanyCommand(companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Company deleted successfully.");
        response.Data.Should().Be(companyId);
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
        context.CompanyRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    /// <summary>
    /// Creates a company entity for delete scenarios.
    /// </summary>
    private static Company CreateCompany(Guid companyId)
    {
        var entity = new Company
        {
            Name = "JOIN CRM",
            TaxId = "RUC-123",
            IsActive = true
        };

        typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(entity, companyId);
        return entity;
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the delete handler.
    /// </summary>
    private sealed class DeleteCompanyCommandTestContext
    {
        public DeleteCompanyCommandTestContext()
        {
            CompanyRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Company>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(CompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();

        public DeleteCompanyCommandHandler CreateHandler()
        {
            return new DeleteCompanyCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
