using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.CreateTicketUserCompany;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies.Commands.CreateTicketUserCompany;

/// <summary>
/// Unit tests for <see cref="CreateTicketUserCompanyCommandHandler"/>.
/// Covers tenant guard, user/tenant membership checks, duplicate detection and persistence.
/// </summary>
public sealed class CreateTicketUserCompanyCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new CreateTicketUserCompanyCommandTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ShouldReturnUserNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketUserCompanyCommandTestContext(companyId);
        context.UserRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync((ApplicationUser?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenUserIsNotMemberOfTenant_ShouldReturnUserNotInTenantError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new CreateTicketUserCompanyCommandTestContext(companyId);
        var userId = _fixture.Create<Guid>();
        context.UserRepositoryMock.Setup(x => x.GetAsync(userId)).ReturnsAsync(new ApplicationUser { Id = userId, Email = "user@join.com" });
        context.UserCompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<UserCompany>());

        var handler = context.CreateHandler();
        var response = await handler.Handle(CreateValidCommand(userId: userId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_NOT_IN_TENANT");
    }

    [Fact]
    public async Task Handle_WhenDuplicateActiveRow_ShouldReturnDuplicateError()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new CreateTicketUserCompanyCommandTestContext(companyId);
        context.UserRepositoryMock.Setup(x => x.GetAsync(userId)).ReturnsAsync(new ApplicationUser { Id = userId, Email = "user@join.com" });
        context.UserCompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
            new List<UserCompany> { new() { UserId = userId, CompanyId = companyId, GcRecord = 0 } });
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
            new List<TicketUserCompany> { new() { UserId = userId, CompanyId = companyId, GcRecord = 0 } });

        var handler = context.CreateHandler();
        var response = await handler.Handle(CreateValidCommand(userId: userId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_USER_COMPANY_DUPLICATE");
    }

    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateAndReturnDto()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new CreateTicketUserCompanyCommandTestContext(companyId);
        context.UserRepositoryMock.Setup(x => x.GetAsync(userId)).ReturnsAsync(new ApplicationUser
        {
            Id = userId,
            FirstName = "Manager",
            LastName = "Test",
            Email = "manager@join.com"
        });
        context.UserCompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
            new List<UserCompany> { new() { UserId = userId, CompanyId = companyId, GcRecord = 0 } });
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketUserCompany>());
        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN" });

        TicketUserCompany? inserted = null;
        context.TicketUserCompanyRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<TicketUserCompany>()))
            .Callback<TicketUserCompany>(entity => inserted = entity)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();
        var response = await handler.Handle(CreateValidCommand(userId: userId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        inserted.Should().NotBeNull();
        inserted!.CompanyId.Should().Be(companyId);
        inserted.UserId.Should().Be(userId);
        inserted.IsSuperAdminTicket.Should().BeTrue();
        inserted.CanFinishTicket.Should().BeTrue();
        inserted.CanResolveTicket.Should().BeFalse();
        response.Data!.UserName.Should().Be("Manager Test");
        response.Data.CompanyName.Should().Be("JOIN");
    }

    [Fact]
    public async Task Handle_WhenFlagsAreAllFalse_ShouldPersistThemAsFalse()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new CreateTicketUserCompanyCommandTestContext(companyId);
        context.UserRepositoryMock.Setup(x => x.GetAsync(userId)).ReturnsAsync(new ApplicationUser { Id = userId, Email = "user@join.com" });
        context.UserCompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
            new List<UserCompany> { new() { UserId = userId, CompanyId = companyId, GcRecord = 0 } });
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketUserCompany>());
        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN" });

        TicketUserCompany? inserted = null;
        context.TicketUserCompanyRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<TicketUserCompany>()))
            .Callback<TicketUserCompany>(entity => inserted = entity)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();
        var response = await handler.Handle(
            CreateValidCommand(userId: userId, isSuperAdminTicket: false, canFinishTicket: false, canResolveTicket: false),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        inserted!.IsSuperAdminTicket.Should().BeFalse();
        inserted.CanFinishTicket.Should().BeFalse();
        inserted.CanResolveTicket.Should().BeFalse();
    }

    private CreateTicketUserCompanyCommand CreateValidCommand(
        Guid? userId = null,
        bool isSuperAdminTicket = true,
        bool canFinishTicket = true,
        bool canResolveTicket = false)
    {
        return new CreateTicketUserCompanyCommand
        {
            UserId = userId ?? _fixture.Create<Guid>(),
            IsSuperAdminTicket = isSuperAdminTicket,
            CanFinishTicket = canFinishTicket,
            CanResolveTicket = canResolveTicket
        };
    }

    private sealed class CreateTicketUserCompanyCommandTestContext
    {
        public CreateTicketUserCompanyCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            TicketUserCompanyRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketUserCompany>())).ReturnsAsync(true);
            UserRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(true);
            SetupRepository(UnitOfWorkMock, TicketUserCompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserCompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketUserCompany>> TicketUserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserCompany>> UserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();

        public CreateTicketUserCompanyCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);

        private static void SetupRepository<TEntity>(Mock<IUnitOfWork> unitOfWorkMock, Mock<IGenericRepository<TEntity>> repositoryMock)
            where TEntity : class
        {
            unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
        }
    }
}
