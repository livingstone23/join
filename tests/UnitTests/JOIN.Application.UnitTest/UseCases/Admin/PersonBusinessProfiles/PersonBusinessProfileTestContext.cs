using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Application.UseCases.Admin.PersonBusinessProfiles;
using JOIN.Domain.Admin;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonBusinessProfiles;

/// <summary>
/// Shared mocks for the person business profile command handler tests. The "single active
/// profile" coordinator is the real implementation over the same mocked repository.
/// </summary>
internal sealed class PersonBusinessProfileTestContext
{
    public static readonly Guid CompanyId = Guid.NewGuid();
    public static readonly Guid PersonId = Guid.NewGuid();

    public PersonBusinessProfileTestContext(Guid companyId)
    {
        CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        UnitOfWorkMock.SetupGet(x => x.PersonBusinessProfiles).Returns(ProfileRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(PersonRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<Industry>()).Returns(IndustryRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<TaxRegime>()).Returns(TaxRegimeRepositoryMock.Object);
        ProfileRepositoryMock
            .Setup(x => x.GetActiveProfilesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
    public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
    public Mock<IPersonBusinessProfileRepository> ProfileRepositoryMock { get; } = new();
    public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = new();
    public Mock<IGenericRepository<Industry>> IndustryRepositoryMock { get; } = new();
    public Mock<IGenericRepository<TaxRegime>> TaxRegimeRepositoryMock { get; } = new();

    public PersonBusinessProfileActiveCoordinator Coordinator => new(ProfileRepositoryMock.Object);

    public void SetupPerson()
        => PersonRepositoryMock.Setup(x => x.GetAsync(PersonId)).ReturnsAsync(new Person { CompanyId = CompanyId, FirstName = "ACME" });

    public void SetupCatalogs()
    {
        IndustryRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync(Industry.Create(CompanyId, "TECH", "Technology", null));
        TaxRegimeRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync(TaxRegime.Create(CompanyId, "GEN", "General"));
    }

    public PersonBusinessProfile SetupExisting()
    {
        var entity = PersonBusinessProfile.Create(CompanyId, PersonId, Guid.NewGuid(), Guid.NewGuid());
        ProfileRepositoryMock
            .Setup(x => x.GetActiveByIdAsync(entity.Id, CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        return entity;
    }
}
