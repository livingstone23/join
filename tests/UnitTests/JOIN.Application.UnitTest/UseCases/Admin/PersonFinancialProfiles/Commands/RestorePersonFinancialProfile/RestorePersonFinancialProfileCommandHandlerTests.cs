using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonFinancialProfiles.Commands.RestorePersonFinancialProfile;

/// <summary>
/// Unit tests for <see cref="RestorePersonFinancialProfileCommandHandler"/> (SPEC 41).
/// </summary>
public sealed class RestorePersonFinancialProfileCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(companyId: Guid.Empty);

        var response = await context.CreateHandler().Handle(new RestorePersonFinancialProfileCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotSuperAdmin_ShouldReturnSuperAdminRequired()
    {
        var context = new TestContext(isSuperAdmin: false);
        var entity = context.SetupDeleted();

        var response = await context.CreateHandler().Handle(new RestorePersonFinancialProfileCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("SUPERADMIN_REQUIRED");
        entity.IsDeleted.Should().BeTrue();
        context.EntityRepositoryMock.Verify(x => x.GetIncludingDeletedAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEntityDoesNotExist_ShouldReturnNotFound()
    {
        var context = new TestContext();

        var response = await context.CreateHandler().Handle(new RestorePersonFinancialProfileCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenEntityIsActive_ShouldReturnNotDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        entity.Restore();

        var response = await context.CreateHandler().Handle(new RestorePersonFinancialProfileCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("NOT_DELETED");
    }

    [Fact]
    public async Task Handle_WhenChecksPass_ShouldRestore()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestorePersonFinancialProfileCommand(entity.Id), CancellationToken.None);

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

        var response = await context.CreateHandler().Handle(new RestorePersonFinancialProfileCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenParentIncomeRange1IsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Parent1.MarkAsDeleted();

        var response = await context.CreateHandler().Handle(new RestorePersonFinancialProfileCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenAnotherActiveRowHoldsTheFlag_ShouldRestoreWithoutIsCurrent()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        entity.IsCurrent.Should().BeTrue();
        var sibling = Apply(PersonFinancialProfile.Create(CompanyId, context.Parent0.Id, Guid.NewGuid(), "Business", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)), p => p.SetAsCurrent());
        context.EntityRepositoryMock.SetupRows([entity, sibling]);
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestorePersonFinancialProfileCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsDeleted.Should().BeFalse();
        entity.IsCurrent.Should().BeFalse("another active row already holds the flag");
    }

    [Fact]
    public async Task Handle_WhenNoOtherActiveRowHoldsTheFlag_ShouldKeepIsCurrent()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestorePersonFinancialProfileCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsCurrent.Should().BeTrue();
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
            UnitOfWorkMock.Setup(x => x.GetRepository<PersonFinancialProfile>()).Returns(EntityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(Parent0RepositoryMock.Object);
            Parent0RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent0.Id)).ReturnsAsync(Parent0);
            UnitOfWorkMock.Setup(x => x.GetRepository<IncomeRange>()).Returns(Parent1RepositoryMock.Object);
            Parent1RepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Parent1.Id)).ReturnsAsync(Parent1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<PersonFinancialProfile>> EntityRepositoryMock { get; } = new();
        public Person Parent0 { get; } = new Person { CompanyId = CompanyId, FirstName = "Jane", LastName = "Doe", IdentificationTypeId = Guid.NewGuid(), IdentificationNumber = "P-1" };
        public Mock<IGenericRepository<Person>> Parent0RepositoryMock { get; } = new();
        public IncomeRange Parent1 { get; } = IncomeRange.Create(CompanyId, "Low", 0m, 1000m, "USD", 1);
        public Mock<IGenericRepository<IncomeRange>> Parent1RepositoryMock { get; } = new();

        public PersonFinancialProfile SetupDeleted()
        {
            var entity = Apply(PersonFinancialProfile.Create(CompanyId, Parent0.Id, Parent1.Id, "Salary", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)), p => p.SetAsCurrent());
            entity.MarkAsDeleted();
            EntityRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(entity.Id)).ReturnsAsync(entity);
            EntityRepositoryMock.SetupRows([entity]);
            return entity;
        }

        public void SetupSaveChanges(int affectedRows)
            => UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(affectedRows);

        public RestorePersonFinancialProfileCommandHandler CreateHandler()
            => new(CurrentUserServiceMock.Object, new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object));
    }
}
