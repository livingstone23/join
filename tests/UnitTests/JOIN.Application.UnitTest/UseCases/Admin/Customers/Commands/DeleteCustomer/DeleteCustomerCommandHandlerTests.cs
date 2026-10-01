using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Customers.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Customers.Commands.DeleteCustomer;

/// <summary>
/// Contains the unit tests for the customer soft-delete command handler.
/// </summary>
public sealed class DeleteCustomerCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(new DeleteCustomerCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCustomerIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);

        var response = await context.CreateHandler().Handle(new DeleteCustomerCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnDeleteFailed()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();

        var response = await context.CreateHandler().Handle(new DeleteCustomerCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenCustomerExists_ShouldSoftDelete()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(new DeleteCustomerCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.GcRecord.Should().NotBe(0);
    }

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            UnitOfWorkMock.Setup(x => x.GetRepository<Customer>()).Returns(CustomerRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Customer>> CustomerRepositoryMock { get; } = new();

        public Customer SetupExisting()
        {
            var entity = Customer.Create(CompanyId, Guid.NewGuid(), Guid.NewGuid(), "C000000001", PersonLifecycleStage.Lead);
            CustomerRepositoryMock.Setup(x => x.GetAsync(entity.Id)).ReturnsAsync(entity);
            return entity;
        }

        public DeleteCustomerCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
