using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonBusinessProfiles.Commands.RestorePersonBusinessProfile;

/// <summary>
/// Unit tests for <see cref="RestorePersonBusinessProfileCommandHandler"/> (SPEC 41).
/// </summary>
public sealed class RestorePersonBusinessProfileCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(companyId: Guid.Empty);

        var response = await context.CreateHandler().Handle(new RestorePersonBusinessProfileCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotSuperAdmin_ShouldReturnSuperAdminRequired()
    {
        var context = new TestContext(isSuperAdmin: false);
        var entity = context.SetupDeleted();

        var response = await context.CreateHandler().Handle(new RestorePersonBusinessProfileCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("SUPERADMIN_REQUIRED");
        entity.IsDeleted.Should().BeTrue();
        context.EntityRepositoryMock.Verify(x => x.GetIncludingDeletedAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEntityDoesNotExist_ShouldReturnNotFound()
    {
        var context = new TestContext();

        var response = await context.CreateHandler().Handle(new RestorePersonBusinessProfileCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenEntityIsActive_ShouldReturnNotDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        entity.Restore();

        var response = await context.CreateHandler().Handle(new RestorePersonBusinessProfileCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("NOT_DELETED");
    }

    [Fact]
    public async Task Handle_WhenChecksPass_ShouldRestore()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestorePersonBusinessProfileCommand(entity.Id), CancellationToken.None);

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

        var response = await context.CreateHandler().Handle(new RestorePersonBusinessProfileCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenParentIndustry1IsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Parent1.MarkAsDeleted();

        var response = await context.CreateHandler().Handle(new RestorePersonBusinessProfileCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenParentTaxRegime2IsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Parent2.MarkAsDeleted();

        var response = await context.CreateHandler().Handle(new RestorePersonBusinessProfileCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenAnotherActiveRowHoldsTheFlag_ShouldRestoreWithoutIsActive()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        entity.IsActive.Should().BeTrue();
        var sibling = PersonBusinessProfile.Create(CompanyId, context.Parent0.Id, Guid.NewGuid(), Guid.NewGuid());
        context.EntityRepositoryMock.SetupRows([entity, sibling]);
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestorePersonBusinessProfileCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsDeleted.Should().BeFalse();
        entity.IsActive.Should().BeFalse("another active row already holds the flag");
    }

    [Fact]
    public async Task Handle_WhenNoOtherActiveRowHoldsTheFlag_ShouldKeepIsActive()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestorePersonBusinessProfileCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsActive.Should().BeTrue();
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
            UnitOfWorkMock.Setup(x => x.GetRepository<PersonBusinessProfile>()).Returns(EntityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(Parent0RepositoryMock.Object);
            Parent0RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent0.Id)).ReturnsAsync(Parent0);
            UnitOfWorkMock.Setup(x => x.GetRepository<Industry>()).Returns(Parent1RepositoryMock.Object);
            Parent1RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent1.Id)).ReturnsAsync(Parent1);
            UnitOfWorkMock.Setup(x => x.GetRepository<TaxRegime>()).Returns(Parent2RepositoryMock.Object);
            Parent2RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent2.Id)).ReturnsAsync(Parent2);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<PersonBusinessProfile>> EntityRepositoryMock { get; } = new();
        public Person Parent0 { get; } = new Person { CompanyId = CompanyId, FirstName = "Jane", LastName = "Doe", IdentificationTypeId = Guid.NewGuid(), IdentificationNumber = "P-1" };
        public Mock<IGenericRepository<Person>> Parent0RepositoryMock { get; } = new();
        public Industry Parent1 { get; } = Industry.Create(CompanyId, "TECH", "Technology", null);
        public Mock<IGenericRepository<Industry>> Parent1RepositoryMock { get; } = new();
        public TaxRegime Parent2 { get; } = TaxRegime.Create(CompanyId, "REG-01", "General");
        public Mock<IGenericRepository<TaxRegime>> Parent2RepositoryMock { get; } = new();

        public PersonBusinessProfile SetupDeleted()
        {
            var entity = PersonBusinessProfile.Create(CompanyId, Parent0.Id, Parent1.Id, Parent2.Id);
            entity.MarkAsDeleted();
            EntityRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(entity.Id)).ReturnsAsync(entity);
            EntityRepositoryMock.SetupRows([entity]);
            return entity;
        }

        public void SetupSaveChanges(int affectedRows)
            => UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(affectedRows);

        public RestorePersonBusinessProfileCommandHandler CreateHandler()
            => new(CurrentUserServiceMock.Object, new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object));
    }
}
