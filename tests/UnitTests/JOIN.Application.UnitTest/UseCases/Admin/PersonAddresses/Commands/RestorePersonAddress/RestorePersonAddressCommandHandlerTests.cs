using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Admin.PersonAddresses.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonAddresses.Commands.RestorePersonAddress;

/// <summary>
/// Unit tests for <see cref="RestorePersonAddressCommandHandler"/> (SPEC 41).
/// </summary>
public sealed class RestorePersonAddressCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(companyId: Guid.Empty);

        var response = await context.CreateHandler().Handle(new RestorePersonAddressCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotSuperAdmin_ShouldReturnSuperAdminRequired()
    {
        var context = new TestContext(isSuperAdmin: false);
        var entity = context.SetupDeleted();

        var response = await context.CreateHandler().Handle(new RestorePersonAddressCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("SUPERADMIN_REQUIRED");
        entity.IsDeleted.Should().BeTrue();
        context.EntityRepositoryMock.Verify(x => x.GetIncludingDeletedAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEntityDoesNotExist_ShouldReturnNotFound()
    {
        var context = new TestContext();

        var response = await context.CreateHandler().Handle(new RestorePersonAddressCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenEntityIsActive_ShouldReturnNotDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        entity.Restore();

        var response = await context.CreateHandler().Handle(new RestorePersonAddressCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("NOT_DELETED");
    }

    [Fact]
    public async Task Handle_WhenChecksPass_ShouldRestore()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestorePersonAddressCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenParentPerson0IsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Parent0.MarkAsDeleted();

        var response = await context.CreateHandler().Handle(new RestorePersonAddressCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenParentCountry1IsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Parent1.MarkAsDeleted();

        var response = await context.CreateHandler().Handle(new RestorePersonAddressCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenParentProvince2IsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Parent2.MarkAsDeleted();

        var response = await context.CreateHandler().Handle(new RestorePersonAddressCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenParentMunicipality3IsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Parent3.MarkAsDeleted();

        var response = await context.CreateHandler().Handle(new RestorePersonAddressCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenParentStreetType4IsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Parent4.MarkAsDeleted();

        var response = await context.CreateHandler().Handle(new RestorePersonAddressCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenParentRegion5IsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Parent5.MarkAsDeleted();

        var response = await context.CreateHandler().Handle(new RestorePersonAddressCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenAnotherActiveRowHoldsTheFlag_ShouldRestoreWithoutIsDefault()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        entity.IsDefault.Should().BeTrue();
        var sibling = Apply(new PersonAddress { CompanyId = CompanyId, PersonId = context.Parent0.Id, AddressLine1 = "Other street", ZipCode = "11002" }, a => a.SetAsDefault());
        context.EntityRepositoryMock.SetupRows([entity, sibling]);
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestorePersonAddressCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsDeleted.Should().BeFalse();
        entity.IsDefault.Should().BeFalse("another active row already holds the flag");
    }

    [Fact]
    public async Task Handle_WhenNoOtherActiveRowHoldsTheFlag_ShouldKeepIsDefault()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestorePersonAddressCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsDefault.Should().BeTrue();
    }

    private static T Apply<T>(T entity, Action<T> mutate)
    {
        mutate(entity);
        return entity;
    }

    private sealed class TestContext
    {
        public TestContext(Guid? companyId = null, bool isSuperAdmin = true)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId ?? CompanyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            UnitOfWorkMock.Setup(x => x.GetRepository<PersonAddress>()).Returns(EntityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(Parent0RepositoryMock.Object);
            Parent0RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent0.Id)).ReturnsAsync(Parent0);
            UnitOfWorkMock.Setup(x => x.GetRepository<Country>()).Returns(Parent1RepositoryMock.Object);
            Parent1RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent1.Id)).ReturnsAsync(Parent1);
            UnitOfWorkMock.Setup(x => x.GetRepository<Province>()).Returns(Parent2RepositoryMock.Object);
            Parent2RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent2.Id)).ReturnsAsync(Parent2);
            UnitOfWorkMock.Setup(x => x.GetRepository<Municipality>()).Returns(Parent3RepositoryMock.Object);
            Parent3RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent3.Id)).ReturnsAsync(Parent3);
            UnitOfWorkMock.Setup(x => x.GetRepository<StreetType>()).Returns(Parent4RepositoryMock.Object);
            Parent4RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent4.Id)).ReturnsAsync(Parent4);
            UnitOfWorkMock.Setup(x => x.GetRepository<Region>()).Returns(Parent5RepositoryMock.Object);
            Parent5RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent5.Id)).ReturnsAsync(Parent5);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<PersonAddress>> EntityRepositoryMock { get; } = new();
        public Person Parent0 { get; } = new Person { CompanyId = CompanyId, FirstName = "Jane", LastName = "Doe", IdentificationTypeId = Guid.NewGuid(), IdentificationNumber = "P-1" };
        public Mock<IGenericRepository<Person>> Parent0RepositoryMock { get; } = new();
        public Country Parent1 { get; } = new Country { Name = "Panama", IsoCode = "PA" };
        public Mock<IGenericRepository<Country>> Parent1RepositoryMock { get; } = new();
        public Province Parent2 { get; } = new Province { Name = "Panama", Code = "PA-8", CountryId = Guid.NewGuid() };
        public Mock<IGenericRepository<Province>> Parent2RepositoryMock { get; } = new();
        public Municipality Parent3 { get; } = new Municipality { Name = "San Miguelito", ProvinceId = Guid.NewGuid() };
        public Mock<IGenericRepository<Municipality>> Parent3RepositoryMock { get; } = new();
        public StreetType Parent4 { get; } = new StreetType { Name = "Avenue", Abbreviation = "Ave" };
        public Mock<IGenericRepository<StreetType>> Parent4RepositoryMock { get; } = new();
        public Region Parent5 { get; } = new Region { CompanyId = CompanyId, CountryId = Guid.NewGuid(), Name = "North" };
        public Mock<IGenericRepository<Region>> Parent5RepositoryMock { get; } = new();

        public PersonAddress SetupDeleted()
        {
            var entity = Apply(new PersonAddress { CompanyId = CompanyId, PersonId = Parent0.Id, AddressLine1 = "Main street", ZipCode = "11001", CountryId = Parent1.Id, ProvinceId = Parent2.Id, MunicipalityId = Parent3.Id, StreetTypeId = Parent4.Id, RegionId = Parent5.Id }, a => a.SetAsDefault());
            entity.MarkAsDeleted();
            EntityRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(entity.Id)).ReturnsAsync(entity);
            EntityRepositoryMock.SetupRows([entity]);
            return entity;
        }

        public void SetupSaveChanges(int affectedRows)
            => UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(affectedRows);

        public RestorePersonAddressCommandHandler CreateHandler()
            => new(CurrentUserServiceMock.Object, new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object));
    }
}
