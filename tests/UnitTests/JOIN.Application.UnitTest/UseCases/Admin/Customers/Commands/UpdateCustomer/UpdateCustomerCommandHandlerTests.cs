using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Customers.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Customers.Commands.UpdateCustomer;

/// <summary>
/// Contains the unit tests for the customer update command handler.
/// </summary>
public sealed class UpdateCustomerCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler()
            .Handle(new UpdateCustomerCommand(Guid.NewGuid(), PersonLifecycleStage.Customer, true), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCustomerBelongsToAnotherTenant_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);
        var foreign = Customer.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "C000000001", PersonLifecycleStage.Lead);
        context.CustomerRepositoryMock.Setup(x => x.GetAsync(foreign.Id)).ReturnsAsync(foreign);

        var response = await context.CreateHandler()
            .Handle(new UpdateCustomerCommand(foreign.Id, PersonLifecycleStage.Customer, true), CancellationToken.None);

        response.Message.Should().Be("CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnUpdateFailed()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();

        var response = await context.CreateHandler()
            .Handle(new UpdateCustomerCommand(entity.Id, PersonLifecycleStage.Customer, true), CancellationToken.None);

        response.Message.Should().Be("UPDATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenDeactivating_ShouldUpdateLifecycleAndResolveNames()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.PersonRepositoryMock.Setup(x => x.GetAsync(entity.PersonId))
            .ReturnsAsync(new Person { CompanyId = CompanyId, PersonType = PersonType.Physical, FirstName = "Ana", LastName = "Lopez" });
        context.UserRepositoryMock.Setup(x => x.GetAsync(entity.UserId))
            .ReturnsAsync(new ApplicationUser { Email = "ana@join.test" });

        var response = await context.CreateHandler()
            .Handle(new UpdateCustomerCommand(entity.Id, PersonLifecycleStage.FormerCustomer, false), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Customer updated successfully.");
        response.Data!.PersonLifecycleStage.Should().Be((int)PersonLifecycleStage.FormerCustomer);
        response.Data.IsActive.Should().BeFalse();
        response.Data.DeactivatedAt.Should().NotBeNull();
        response.Data.PersonName.Should().Be("Ana Lopez");
        response.Data.UserEmail.Should().Be("ana@join.test");
    }

    [Fact]
    public async Task Handle_WhenReactivatingWithMissingRelatedRecords_ShouldReturnEmptyNames()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        entity.Deactivate();
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler()
            .Handle(new UpdateCustomerCommand(entity.Id, PersonLifecycleStage.Customer, true), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.IsActive.Should().BeTrue();
        response.Data.PersonName.Should().BeEmpty();
        response.Data.UserEmail.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenPersonIsLegal_ShouldUseCommercialName()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.PersonRepositoryMock.Setup(x => x.GetAsync(entity.PersonId))
            .ReturnsAsync(new Person { CompanyId = CompanyId, PersonType = PersonType.Legal, FirstName = "ACME", CommercialName = "ACME S.A." });

        var response = await context.CreateHandler()
            .Handle(new UpdateCustomerCommand(entity.Id, PersonLifecycleStage.Customer, true), CancellationToken.None);

        response.Data!.PersonName.Should().Be("ACME S.A.");
    }

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            UnitOfWorkMock.Setup(x => x.GetRepository<Customer>()).Returns(CustomerRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(PersonRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationUser>()).Returns(UserRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Customer>> CustomerRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = new();

        public Customer SetupExisting()
        {
            var entity = Customer.Create(CompanyId, Guid.NewGuid(), Guid.NewGuid(), "C000000001", PersonLifecycleStage.Lead);
            CustomerRepositoryMock.Setup(x => x.GetAsync(entity.Id)).ReturnsAsync(entity);
            return entity;
        }

        public UpdateCustomerCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
