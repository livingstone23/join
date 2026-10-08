using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Admin.Persons;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Persons;

/// <summary>
/// Unit tests for <see cref="PersonCascadeCoordinator.RestoreChildrenAsync"/> (SPEC 41, decision 2026-10-08):
/// restoring a person brings back the composition children of the same cascade, with validation first.
/// </summary>
public sealed class PersonCascadeCoordinatorTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid PersonId = Guid.NewGuid();
    private static readonly DateTime CascadeDay = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EarlierDay = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly int Stamp = BaseAuditableEntity.GetDeletionGcRecordStamp(CascadeDay);

    [Fact]
    public async Task RestoreChildrenAsync_ShouldRestoreOnlyTheChildrenOfTheSameCascade()
    {
        var context = new TestContext();
        var sameCascade = context.Contact("jane@x.com", CascadeDay);
        var deletedBefore = context.Contact("old@x.com", EarlierDay);
        var employment = PersonEmployment.Create(CompanyId, PersonId, "JOIN", "Engineer", EarlierDay);
        employment.MarkAsDeleted(CascadeDay);
        context.Rows<PersonContact>(sameCascade, deletedBefore);
        context.Rows<PersonEmployment>(employment);

        var error = await context.Coordinator.RestoreChildrenAsync(PersonId, Stamp);

        error.Should().BeNull();
        sameCascade.IsDeleted.Should().BeFalse();
        employment.IsDeleted.Should().BeFalse();
        deletedBefore.IsDeleted.Should().BeTrue("children deleted one by one before the cascade stay deleted");
    }

    [Fact]
    public async Task RestoreChildrenAsync_WhenAnAddressCatalogIsDeleted_ShouldAbortWithParentDeleted()
    {
        var context = new TestContext();
        var address = context.Address(isDefault: false);
        context.Rows<PersonAddress>(address);
        context.Catalog<Country>(address.CountryId, deleted: true);

        var error = await context.Coordinator.RestoreChildrenAsync(PersonId, Stamp);

        error!.Message.Should().Be("PARENT_DELETED");
        address.IsDeleted.Should().BeTrue("nothing is restored when a child fails validation");
    }

    [Fact]
    public async Task RestoreChildrenAsync_WhenAProfileCatalogIsDeleted_ShouldAbortWithParentDeleted()
    {
        var context = new TestContext();
        var business = PersonBusinessProfile.Create(CompanyId, PersonId, Guid.NewGuid(), Guid.NewGuid());
        business.MarkAsDeleted(CascadeDay);
        var financial = PersonFinancialProfile.Create(CompanyId, PersonId, Guid.NewGuid(), "Salary", EarlierDay);
        financial.MarkAsDeleted(CascadeDay);
        context.Rows<PersonBusinessProfile>(business);
        context.Rows<PersonFinancialProfile>(financial);
        context.Catalog<Industry>(business.IndustryId, deleted: false);
        context.Catalog<TaxRegime>(business.TaxRegimeId, deleted: true);
        context.Catalog<IncomeRange>(financial.IncomeRangeId, deleted: true);

        var error = await context.Coordinator.RestoreChildrenAsync(PersonId, Stamp);

        error!.Message.Should().Be("PARENT_DELETED");
        error.Errors.Should().HaveCount(2);
    }

    [Fact]
    public async Task RestoreChildrenAsync_WhenAContactIsAlreadyActive_ShouldAbortWithActiveDuplicate()
    {
        var context = new TestContext();
        var deleted = context.Contact("jane@x.com", CascadeDay);
        var active = PersonContact.Create(CompanyId, PersonId, ContactType.PrimaryEmail, "jane@x.com");
        context.Rows<PersonContact>(deleted, active);

        var error = await context.Coordinator.RestoreChildrenAsync(PersonId, Stamp);

        error!.Message.Should().Be("ACTIVE_DUPLICATE_EXISTS");
        deleted.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreChildrenAsync_WhenTwoDeletedContactsShareTheKey_ShouldAbortWithActiveDuplicate()
    {
        var context = new TestContext();
        context.Rows<PersonContact>(context.Contact("jane@x.com", CascadeDay), context.Contact("jane@x.com", CascadeDay));

        var error = await context.Coordinator.RestoreChildrenAsync(PersonId, Stamp);

        error!.Message.Should().Be("ACTIVE_DUPLICATE_EXISTS");
    }

    [Fact]
    public async Task RestoreChildrenAsync_WhenAnotherDefaultAddressIsActive_ShouldRestoreWithoutTheFlag()
    {
        var context = new TestContext();
        var restored = context.Address(isDefault: true);
        var activeDefault = new PersonAddress { CompanyId = CompanyId, PersonId = PersonId, AddressLine1 = "Other", ZipCode = "2" };
        activeDefault.SetAsDefault();
        context.Rows<PersonAddress>(restored, activeDefault);
        context.ActiveAddressCatalogs(restored);

        var error = await context.Coordinator.RestoreChildrenAsync(PersonId, Stamp);

        error.Should().BeNull();
        restored.IsDeleted.Should().BeFalse();
        restored.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreChildrenAsync_ShouldKeepASingleFlagHolderPerKind()
    {
        var context = new TestContext();
        var primaryA = context.Contact("a@x.com", CascadeDay, primary: true);
        var primaryB = context.Contact("b@x.com", CascadeDay, primary: true);
        var currentA = PersonEmployment.Create(CompanyId, PersonId, "JOIN", "Engineer", EarlierDay);
        currentA.SetAsCurrent();
        currentA.MarkAsDeleted(CascadeDay);
        var currentB = PersonEmployment.Create(CompanyId, PersonId, "ACME", "Manager", EarlierDay);
        currentB.SetAsCurrent();
        currentB.MarkAsDeleted(CascadeDay);
        var activeBusiness = PersonBusinessProfile.Create(CompanyId, PersonId, Guid.NewGuid(), Guid.NewGuid());
        var restoredBusiness = PersonBusinessProfile.Create(CompanyId, PersonId, Guid.NewGuid(), Guid.NewGuid());
        restoredBusiness.MarkAsDeleted(CascadeDay);
        var activeFinancial = PersonFinancialProfile.Create(CompanyId, PersonId, Guid.NewGuid(), "Salary", EarlierDay);
        activeFinancial.SetAsCurrent();
        var restoredFinancial = PersonFinancialProfile.Create(CompanyId, PersonId, Guid.NewGuid(), "Business", EarlierDay);
        restoredFinancial.SetAsCurrent();
        restoredFinancial.MarkAsDeleted(CascadeDay);
        context.Rows<PersonContact>(primaryA, primaryB);
        context.Rows<PersonEmployment>(currentA, currentB);
        context.Rows<PersonBusinessProfile>(activeBusiness, restoredBusiness);
        context.Rows<PersonFinancialProfile>(activeFinancial, restoredFinancial);
        context.Catalog<Industry>(restoredBusiness.IndustryId, deleted: false);
        context.Catalog<TaxRegime>(restoredBusiness.TaxRegimeId, deleted: false);
        context.Catalog<IncomeRange>(restoredFinancial.IncomeRangeId, deleted: false);

        var error = await context.Coordinator.RestoreChildrenAsync(PersonId, Stamp);

        error.Should().BeNull();
        new[] { primaryA.IsPrimary, primaryB.IsPrimary }.Should().ContainSingle(isPrimary => isPrimary);
        new[] { currentA.IsCurrent, currentB.IsCurrent }.Should().ContainSingle(isCurrent => isCurrent);
        restoredBusiness.IsActive.Should().BeFalse("another business profile is already active");
        restoredFinancial.IsCurrent.Should().BeFalse("another financial profile is already current");
    }

    private sealed class TestContext
    {
        public TestContext()
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);
            Coordinator = new PersonCascadeCoordinator(
                UnitOfWorkMock.Object,
                new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object));
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public PersonCascadeCoordinator Coordinator { get; }

        public void Rows<T>(params T[] rows)
            where T : class
            => UnitOfWorkMock.SetupRepositoryRows<T>(rows);

        public void Catalog<T>(Guid id, bool deleted)
            where T : BaseAuditableEntity, new()
        {
            var catalog = new T();
            typeof(BaseEntity).GetProperty("Id")!.SetValue(catalog, id);
            if (deleted)
            {
                catalog.MarkAsDeleted();
            }

            var repositoryMock = new Mock<IGenericRepository<T>>();
            repositoryMock.Setup(x => x.GetIncludingDeletedAsync(id)).ReturnsAsync(catalog);
            UnitOfWorkMock.Setup(x => x.GetRepository<T>()).Returns(repositoryMock.Object);
        }

        public PersonContact Contact(string value, DateTime deletedOn, bool primary = false)
        {
            var contact = PersonContact.Create(CompanyId, PersonId, ContactType.PrimaryEmail, value);
            if (primary)
            {
                contact.SetAsPrimary();
            }

            contact.MarkAsDeleted(deletedOn);
            return contact;
        }

        public PersonAddress Address(bool isDefault)
        {
            var address = new PersonAddress
            {
                CompanyId = CompanyId,
                PersonId = PersonId,
                AddressLine1 = "Main street",
                ZipCode = "11001",
                CountryId = Guid.NewGuid(),
                ProvinceId = Guid.NewGuid(),
                MunicipalityId = Guid.NewGuid(),
                StreetTypeId = Guid.NewGuid()
            };
            if (isDefault)
            {
                address.SetAsDefault();
            }

            address.MarkAsDeleted(CascadeDay);
            return address;
        }

        public void ActiveAddressCatalogs(PersonAddress address)
        {
            Catalog<Country>(address.CountryId, deleted: false);
            Catalog<Province>(address.ProvinceId, deleted: false);
            Catalog<Municipality>(address.MunicipalityId, deleted: false);
            Catalog<StreetType>(address.StreetTypeId, deleted: false);
        }
    }
}
