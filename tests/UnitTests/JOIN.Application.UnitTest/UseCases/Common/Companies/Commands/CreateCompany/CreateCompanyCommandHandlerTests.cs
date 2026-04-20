using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Common.Companies.Commands;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Companies.Commands.CreateCompany;

/// <summary>
/// Contains the unit tests for the company creation command.
/// These tests verify duplicate tax id validation, seeding, and persistence behavior.
/// </summary>
public sealed class CreateCompanyCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the duplicate-tax-id branch using a case-insensitive comparison.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTaxIdAlreadyExists_ShouldReturnTaxIdInUseError()
    {
        // Arrange
        var context = new CreateCompanyCommandTestContext();
        context.CompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            new Company { Name = "Other", TaxId = "RUC-123", GcRecord = 0 }
        });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand() with { TaxId = "  ruc-123  " }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_TAXID_IN_USE");
        response.Errors.Should().Contain("Another active company already uses the same TaxId.");
        context.CatalogSeederMock.Verify(x => x.SeedDefaultCatalogsForCompanyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when the save operation affects no rows.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        // Arrange
        var context = new CreateCompanyCommandTestContext();
        context.CompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Company>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
        response.Errors.Should().Contain("No records were affected while creating the company.");
        context.CatalogSeederMock.Verify(x => x.SeedDefaultCatalogsForCompanyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the successful creation flow, trimming behavior, and catalog seeding side effect.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateCompanySeedCatalogsAndReturnDto()
    {
        // Arrange
        var context = new CreateCompanyCommandTestContext();
        Company? insertedEntity = null;
        var createdId = _fixture.Create<Guid>();

        context.CompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Company>());
        context.CompanyRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<Company>()))
            .Callback<Company>(entity =>
            {
                insertedEntity = entity;
                typeof(JOIN.Domain.Audit.BaseEntity).GetProperty("Id")!.SetValue(entity, createdId);
            })
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand() with
        {
            Name = "  JOIN CRM  ",
            Description = "  CRM Platform  ",
            TaxId = "  RUC-123  ",
            Email = "  info@join.com  ",
            Phone = "  555-1000  ",
            WebSite = "  https://join.com  "
        }, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Company created successfully.");
        insertedEntity.Should().NotBeNull();
        insertedEntity!.Name.Should().Be("JOIN CRM");
        insertedEntity.Description.Should().Be("CRM Platform");
        insertedEntity.TaxId.Should().Be("RUC-123");
        insertedEntity.Email.Should().Be("info@join.com");
        insertedEntity.Phone.Should().Be("555-1000");
        insertedEntity.WebSite.Should().Be("https://join.com");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(createdId);
        response.Data.Name.Should().Be("JOIN CRM");
        response.Data.TaxId.Should().Be("RUC-123");
        context.CatalogSeederMock.Verify(x => x.SeedDefaultCatalogsForCompanyAsync(createdId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid command for company creation scenarios.
    /// </summary>
    private static CreateCompanyCommand CreateValidCommand()
    {
        return new CreateCompanyCommand
        {
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
    /// Holds the reusable mocks and helper factory for the create handler.
    /// </summary>
    private sealed class CreateCompanyCommandTestContext
    {
        public CreateCompanyCommandTestContext()
        {
            CompanyRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<Company>())).ReturnsAsync(true);
            CatalogSeederMock.Setup(x => x.SeedDefaultCatalogsForCompanyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(CompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<ICompanyCatalogSeeder> CatalogSeederMock { get; } = new();

        public CreateCompanyCommandHandler CreateHandler()
        {
            return new CreateCompanyCommandHandler(UnitOfWorkMock.Object, CatalogSeederMock.Object);
        }
    }
}
