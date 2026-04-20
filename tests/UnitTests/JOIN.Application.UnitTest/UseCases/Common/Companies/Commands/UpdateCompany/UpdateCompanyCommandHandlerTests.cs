using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Companies.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Companies.Commands.UpdateCompany;

/// <summary>
/// Contains the unit tests for the company update command.
/// These tests verify not-found handling, duplicate tax id protection, and persistence behavior.
/// </summary>
public sealed class UpdateCompanyCommandHandlerTests
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
        var context = new UpdateCompanyCommandTestContext();
        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync((Company?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_NOT_FOUND");
        response.Errors.Should().Contain("Company not found.");
    }

    /// <summary>
    /// Verifies the duplicate-tax-id branch using a case-insensitive comparison that excludes the current company.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherCompanyUsesSameTaxId_ShouldReturnTaxIdInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var existing = CreateCompany(companyId, "JOIN CRM", "RUC-111");
        var other = CreateCompany(_fixture.Create<Guid>(), "Other", "RUC-123");
        var context = new UpdateCompanyCommandTestContext();

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(existing);
        context.CompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { existing, other });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(companyId) with { TaxId = "  ruc-123  " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_TAXID_IN_USE");
        response.Errors.Should().Contain("Another active company already uses the same TaxId.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the save operation affects no rows.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var existing = CreateCompany(companyId, "JOIN CRM", "RUC-111");
        var context = new UpdateCompanyCommandTestContext();

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(existing);
        context.CompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { existing });
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UPDATE_FAILED");
        response.Errors.Should().Contain("No records were affected while updating the company.");
    }

    /// <summary>
    /// Verifies the successful update flow and field trimming behavior.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateCompanyAndReturnDto()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var existing = CreateCompany(companyId, "Old", "OLD-1");
        var context = new UpdateCompanyCommandTestContext();

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(existing);
        context.CompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { existing });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(companyId) with
        {
            Name = "  JOIN CRM  ",
            Description = "  CRM Platform  ",
            TaxId = "  RUC-123  ",
            Email = "  info@join.com  ",
            Phone = "  555-1000  ",
            WebSite = "  https://join.com  ",
            IsActive = false
        }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Company updated successfully.");
        existing.Name.Should().Be("JOIN CRM");
        existing.Description.Should().Be("CRM Platform");
        existing.TaxId.Should().Be("RUC-123");
        existing.Email.Should().Be("info@join.com");
        existing.Phone.Should().Be("555-1000");
        existing.WebSite.Should().Be("https://join.com");
        existing.IsActive.Should().BeFalse();
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(companyId);
        response.Data.IsActive.Should().BeFalse();
        context.CompanyRepositoryMock.Verify(x => x.UpdateAsync(existing), Times.Once);
    }

    /// <summary>
    /// Creates a valid command for company update scenarios.
    /// </summary>
    private static UpdateCompanyCommand CreateValidCommand(Guid companyId)
    {
        return new UpdateCompanyCommand
        {
            Id = companyId,
            Name = "JOIN CRM",
            Description = "CRM Platform",
            TaxId = "RUC-123",
            Email = "info@join.com",
            Phone = "555-1000",
            WebSite = "https://join.com",
            IsActive = true
        };
    }

    /// <summary>
    /// Creates a company entity for update scenarios.
    /// </summary>
    private static Company CreateCompany(Guid companyId, string name, string taxId)
    {
        var entity = new Company
        {
            Name = name,
            TaxId = taxId,
            IsActive = true
        };

        typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(entity, companyId);
        return entity;
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the update handler.
    /// </summary>
    private sealed class UpdateCompanyCommandTestContext
    {
        public UpdateCompanyCommandTestContext()
        {
            CompanyRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Company>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(CompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();

        public UpdateCompanyCommandHandler CreateHandler()
        {
            return new UpdateCompanyCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
