using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Application.UseCases.Admin.PersonEmployments;
using JOIN.Domain.Admin;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonEmployments;

/// <summary>
/// Shared mocks for the person employment command handler tests. The "current employment"
/// coordinator is the real implementation over the same mocked repository.
/// </summary>
internal sealed class PersonEmploymentTestContext
{
    public static readonly Guid CompanyId = Guid.NewGuid();
    public static readonly Guid PersonId = Guid.NewGuid();
    public static readonly DateTime StartDate = new(2024, 1, 15);

    public PersonEmploymentTestContext(Guid companyId)
    {
        CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        UnitOfWorkMock.SetupGet(x => x.PersonEmployments).Returns(EmploymentRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(PersonRepositoryMock.Object);
        EmploymentRepositoryMock
            .Setup(x => x.GetActiveCurrentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
    public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
    public Mock<IPersonEmploymentRepository> EmploymentRepositoryMock { get; } = new();
    public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = new();

    public PersonEmploymentCurrentCoordinator Coordinator => new(EmploymentRepositoryMock.Object);

    public void SetupPerson()
        => PersonRepositoryMock.Setup(x => x.GetAsync(PersonId)).ReturnsAsync(new Person { CompanyId = CompanyId, FirstName = "Ana" });

    public PersonEmployment SetupExisting(bool isCurrent)
    {
        var entity = PersonEmployment.Create(CompanyId, PersonId, "ACME", "Dev", StartDate);
        if (isCurrent)
        {
            entity.SetAsCurrent();
        }

        EmploymentRepositoryMock
            .Setup(x => x.GetActiveByIdAsync(entity.Id, CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        return entity;
    }
}
