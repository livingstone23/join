using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Admin;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Admin.Customers.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Customers.Commands.CreateCustomer;

/// <summary>
/// Contains the unit tests for the customer creation command handler.
/// </summary>
public sealed class CreateCustomerCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid PersonId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnInvalidCompany()
    {
        var context = new TestContext(CompanyId);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("INVALID_COMPANY");
    }

    [Fact]
    public async Task Handle_WhenPersonBelongsToAnotherTenant_ShouldReturnInvalidPerson()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.PersonRepositoryMock.Setup(x => x.GetAsync(PersonId))
            .ReturnsAsync(new Person { CompanyId = Guid.NewGuid(), FirstName = "Ana" });

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("INVALID_PERSON");
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ShouldReturnInvalidUser()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.SetupPerson(PersonType.Physical);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("INVALID_USER");
        response.Errors.Should().Contain("User not found.");
    }

    [Fact]
    public async Task Handle_WhenUserIsNotLinkedToTenant_ShouldReturnInvalidUser()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.SetupPerson(PersonType.Physical);
        context.SetupUser();
        context.UserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([new UserCompany { UserId = UserId, CompanyId = Guid.NewGuid() }]);
        // SPEC 38: build the handler LAST so the negative IsActiveMemberAsync setup wins over
        // the default true from CreateHandler (Moq last-write semantics).
        var handler = context.CreateHandler();
        context.UserCompanyNamedRepositoryMock
            .Setup(x => x.IsActiveMemberAsync(UserId, CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var response = await handler.Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("INVALID_USER");
        response.Errors.Should().Contain("User is not linked to the current company.");
    }

    [Fact]
    public async Task Handle_WhenCustomerLinkExists_ShouldReturnLinkExists()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.SetupPerson(PersonType.Physical);
        context.SetupUser();
        context.SetupUserLinked();
        context.CustomerRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([Customer.Create(CompanyId, PersonId, UserId, "C000000001", PersonLifecycleStage.Lead)]);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("CUSTOMER_LINK_EXISTS");
        context.CodeGeneratorMock.Verify(x => x.GenerateNextAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnCreateFailed()
    {
        var context = new TestContext(CompanyId);
        context.SetupHappyPath(PersonType.Physical);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("CREATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenPhysicalPerson_ShouldCreateCustomerWithFullName()
    {
        var context = new TestContext(CompanyId);
        context.SetupHappyPath(PersonType.Physical);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Customer created successfully.");
        response.Data!.CustomerCode.Should().Be("C000000007");
        response.Data.PersonName.Should().Be("Ana Maria Lopez");
        response.Data.UserEmail.Should().Be("ana@join.test");
        response.Data.PersonLifecycleStage.Should().Be((int)PersonLifecycleStage.Prospect);
        response.Data.PersonLifecycleStageName.Should().Be("En proceso");
        response.Data.IsActive.Should().BeTrue();
        context.CustomerRepositoryMock.Verify(x => x.InsertAsync(It.Is<Customer>(c => c.PersonId == PersonId && c.UserId == UserId)), Times.Once);
    }

    [Theory]
    [InlineData("ACME S.A.", "ACME S.A.")]
    [InlineData(null, "Ana")]
    public async Task Handle_WhenLegalPerson_ShouldUseCommercialNameOrFirstName(string? commercialName, string expected)
    {
        var context = new TestContext(CompanyId);
        context.SetupHappyPath(PersonType.Legal, commercialName);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Data!.PersonName.Should().Be(expected);
    }

    private static CreateCustomerCommand Command() => new(PersonId, UserId, PersonLifecycleStage.Prospect);

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(CompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(PersonRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationUser>()).Returns(UserRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<UserCompany>()).Returns(UserCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Customer>()).Returns(CustomerRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ICustomerCodeGenerator> CodeGeneratorMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserCompany>> UserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Customer>> CustomerRepositoryMock { get; } = new();
        public Mock<IUserCompanyRepository> UserCompanyNamedRepositoryMock { get; } = new();

        public void SetupCompany()
            => CompanyRepositoryMock.Setup(x => x.GetAsync(CompanyId))
                .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        public void SetupPerson(PersonType type, string? commercialName = null)
            => PersonRepositoryMock.Setup(x => x.GetAsync(PersonId)).ReturnsAsync(new Person
            {
                CompanyId = CompanyId,
                PersonType = type,
                FirstName = "Ana",
                MiddleName = "Maria",
                LastName = "Lopez",
                SecondLastName = " ",
                CommercialName = commercialName
            });

        public void SetupUser()
            => UserRepositoryMock.Setup(x => x.GetAsync(UserId))
                .ReturnsAsync(new ApplicationUser { Id = UserId, Email = "ana@join.test" });

        public void SetupUserLinked()
            => UserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
                .ReturnsAsync([new UserCompany { UserId = UserId, CompanyId = CompanyId }]);

        public void SetupHappyPath(PersonType type, string? commercialName = null)
        {
            SetupCompany();
            SetupPerson(type, commercialName);
            SetupUser();
            SetupUserLinked();
            CustomerRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);
            CodeGeneratorMock.Setup(x => x.GenerateNextAsync(CompanyId, It.IsAny<CancellationToken>())).ReturnsAsync("C000000007");
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public CreateCustomerCommandHandler CreateHandler()
        {
            // SPEC 38: tenant membership check via the named repo. Default to "linked" so
            // existing happy-path tests keep passing; tests that need "not linked" override.
            UserCompanyNamedRepositoryMock
                .Setup(x => x.IsActiveMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.UserCompanies).Returns(UserCompanyNamedRepositoryMock.Object);

            return new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object, CodeGeneratorMock.Object);
        }
    }
}
