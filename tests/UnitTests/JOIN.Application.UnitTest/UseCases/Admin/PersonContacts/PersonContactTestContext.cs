using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Application.UseCases.Admin.PersonContacts;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonContacts;

/// <summary>
/// Shared mocks for the person contact command handler tests. The primary coordinator is
/// the real implementation over the same mocked contact repository.
/// </summary>
internal sealed class PersonContactTestContext
{
    public static readonly Guid CompanyId = Guid.NewGuid();
    public static readonly Guid PersonId = Guid.NewGuid();

    public PersonContactTestContext(Guid companyId)
    {
        CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        UnitOfWorkMock.SetupGet(x => x.PersonContacts).Returns(ContactRepositoryMock.Object);
        UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(PersonRepositoryMock.Object);
        ContactRepositoryMock
            .Setup(x => x.GetActiveWithPrimaryByTypeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ContactType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
    public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
    public Mock<IPersonContactRepository> ContactRepositoryMock { get; } = new();
    public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = new();

    public PersonContactPrimaryCoordinator Coordinator => new(ContactRepositoryMock.Object);

    public void SetupPerson()
        => PersonRepositoryMock.Setup(x => x.GetAsync(PersonId)).ReturnsAsync(new Person { CompanyId = CompanyId, FirstName = "Ana" });

    public PersonContact SetupExistingContact(bool isPrimary, ContactType type = ContactType.PrimaryEmail)
    {
        var entity = PersonContact.Create(CompanyId, PersonId, type, "old@join.test");
        if (isPrimary)
        {
            entity.SetAsPrimary();
        }

        ContactRepositoryMock
            .Setup(x => x.GetActiveByIdAsync(entity.Id, CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        return entity;
    }
}
