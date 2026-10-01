using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Application.UseCases.Admin.PersonAddresses;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonAddresses;

/// <summary>
/// Shared mocks for the person address command handler tests. The default coordinator
/// is the real implementation wired to the same mocked address repository, so the
/// "only one default address" invariant is exercised end-to-end.
/// </summary>
internal sealed class PersonAddressTestContext
{
    public static readonly Guid CompanyId = Guid.NewGuid();
    public static readonly Guid PersonId = Guid.NewGuid();

    public PersonAddressTestContext(Guid companyId)
    {
        CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        UnitOfWorkMock.SetupGet(x => x.PersonAddresses).Returns(AddressRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(PersonRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<Country>()).Returns(CountryRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<StreetType>()).Returns(StreetTypeRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<Region>()).Returns(RegionRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<Province>()).Returns(ProvinceRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<Municipality>()).Returns(MunicipalityRepositoryMock.Object);
        AddressRepositoryMock
            .Setup(x => x.GetActiveWithDefaultAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
    public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
    public Mock<IPersonAddressRepository> AddressRepositoryMock { get; } = new();
    public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = new();
    public Mock<IGenericRepository<Country>> CountryRepositoryMock { get; } = new();
    public Mock<IGenericRepository<StreetType>> StreetTypeRepositoryMock { get; } = new();
    public Mock<IGenericRepository<Region>> RegionRepositoryMock { get; } = new();
    public Mock<IGenericRepository<Province>> ProvinceRepositoryMock { get; } = new();
    public Mock<IGenericRepository<Municipality>> MunicipalityRepositoryMock { get; } = new();

    public PersonAddressDefaultCoordinator Coordinator => new(AddressRepositoryMock.Object);

    public void SetupPerson()
        => PersonRepositoryMock.Setup(x => x.GetAsync(PersonId)).ReturnsAsync(new Person { CompanyId = CompanyId, FirstName = "Ana" });

    public void SetupAllReferences()
    {
        CountryRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync(new Country());
        StreetTypeRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync(new StreetType());
        RegionRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync(new Region());
        ProvinceRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync(new Province());
        MunicipalityRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync(new Municipality());
    }

    public PersonAddress SetupExistingAddress(bool isDefault)
    {
        var entity = new PersonAddress { CompanyId = CompanyId, PersonId = PersonId, AddressLine1 = "Old", ZipCode = "000" };
        if (isDefault)
        {
            entity.SetAsDefault();
        }

        AddressRepositoryMock
            .Setup(x => x.GetActiveByIdAsync(entity.Id, CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        return entity;
    }
}
