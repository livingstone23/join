using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles;
using JOIN.Domain.Admin;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonFinancialProfiles;

/// <summary>
/// Shared mocks for the person financial profile command handler tests. The "current
/// profile" coordinator is the real implementation over the same mocked repository.
/// </summary>
internal sealed class PersonFinancialProfileTestContext
{
    public static readonly Guid CompanyId = Guid.NewGuid();
    public static readonly Guid PersonId = Guid.NewGuid();
    public static readonly DateTime DeclaredDate = new(2025, 6, 1);

    public PersonFinancialProfileTestContext(Guid companyId)
    {
        CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        UnitOfWorkMock.SetupGet(x => x.PersonFinancialProfiles).Returns(ProfileRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(PersonRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<IncomeRange>()).Returns(IncomeRangeRepositoryMock.Object);
        ProfileRepositoryMock
            .Setup(x => x.GetActiveCurrentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        IncomeRange = IncomeRange.Create(CompanyId, "Low", 0, 1000, "USD", 1);
    }

    public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
    public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
    public Mock<IPersonFinancialProfileRepository> ProfileRepositoryMock { get; } = new();
    public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = new();
    public Mock<IGenericRepository<IncomeRange>> IncomeRangeRepositoryMock { get; } = new();
    public IncomeRange IncomeRange { get; }

    public PersonFinancialProfileCurrentCoordinator Coordinator => new(ProfileRepositoryMock.Object);

    public void SetupPerson()
        => PersonRepositoryMock.Setup(x => x.GetAsync(PersonId)).ReturnsAsync(new Person { CompanyId = CompanyId, FirstName = "Ana" });

    public void SetupIncomeRange()
        => IncomeRangeRepositoryMock.Setup(x => x.GetAsync(IncomeRange.Id)).ReturnsAsync(IncomeRange);

    public PersonFinancialProfile SetupExisting(bool isCurrent)
    {
        var entity = PersonFinancialProfile.Create(CompanyId, PersonId, IncomeRange.Id, "Salary", DeclaredDate);
        if (isCurrent)
        {
            entity.SetAsCurrent();
        }

        ProfileRepositoryMock
            .Setup(x => x.GetActiveByIdAsync(entity.Id, CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        return entity;
    }
}
