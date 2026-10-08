using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.RestoreTicketUserCompany;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies.Commands.RestoreTicketUserCompany;

/// <summary>
/// Unit tests for <see cref="RestoreTicketUserCompanyCommandHandler"/> (SPEC 41, Etapa 4).
/// </summary>
public sealed class RestoreTicketUserCompanyCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(companyId: Guid.Empty);

        var response = await context.HandleAsync(Guid.NewGuid());

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotSuperAdmin_ShouldReturnSuperAdminRequired()
    {
        var context = new TestContext(isSuperAdmin: false);
        var entity = context.SetupDeleted();

        var response = await context.HandleAsync(entity.Id);

        response.Message.Should().Be("SUPERADMIN_REQUIRED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenEntityBelongsToAnotherCompany_ShouldReturnNotFound()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted(companyId: Guid.NewGuid());

        var response = await context.HandleAsync(entity.Id);

        response.Message.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenChecksPass_ShouldRestore()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.HandleAsync(entity.Id);

        response.IsSuccess.Should().BeTrue();
        entity.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenActiveDuplicateExists_ShouldReturnActiveDuplicateExists()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.EntityRepositoryMock.SetupRows([entity, context.ActiveTwinOf(entity)]);

        var response = await context.HandleAsync(entity.Id);

        response.Message.Should().Be("ACTIVE_DUPLICATE_EXISTS");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenUserIsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.User.GcRecord = 20260101;

        var response = await context.HandleAsync(entity.Id);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenUserIsNoLongerMemberOfTheCompany_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.Membership.MarkAsDeleted();

        var response = await context.HandleAsync(entity.Id);

        response.Message.Should().Be("PARENT_DELETED");
        entity.IsDeleted.Should().BeTrue();
    }

    private sealed class TestContext
    {
        public TestContext(Guid? companyId = null, bool isSuperAdmin = true)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId ?? CompanyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketUserCompany>()).Returns(EntityRepositoryMock.Object);
            User = Parent(new ApplicationUser { Id = Guid.NewGuid() });
            Membership = new UserCompany { CompanyId = CompanyId, UserId = User.Id };
            UnitOfWorkMock.SetupRepositoryRows<UserCompany>([Membership]);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketUserCompany>> EntityRepositoryMock { get; } = new();
        public ApplicationUser User { get; private set; } = null!;
        public UserCompany Membership { get; private set; } = null!;

        public TicketUserCompany SetupDeleted(Guid? companyId = null)
        {
            var entity = new TicketUserCompany { CompanyId = companyId ?? CompanyId, UserId = User.Id };
            entity.MarkAsDeleted();
            EntityRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(entity.Id)).ReturnsAsync(entity);
            EntityRepositoryMock.SetupRows([entity]);
            return entity;
        }

        public TicketUserCompany ActiveTwinOf(TicketUserCompany entity) => new TicketUserCompany { CompanyId = entity.CompanyId, UserId = entity.UserId };

        public Task<Response<Guid>> HandleAsync(Guid id)
            => new RestoreTicketUserCompanyCommandHandler(CurrentUserServiceMock.Object, new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object))
                .Handle(new RestoreTicketUserCompanyCommand(id), CancellationToken.None);

        private T Parent<T>(T parent)
            where T : class, IAuditableEntity
        {
            var repositoryMock = new Mock<IGenericRepository<T>>();
            repositoryMock.Setup(x => x.GetIncludingDeletedAsync(It.IsAny<Guid>())).ReturnsAsync(parent);
            UnitOfWorkMock.Setup(x => x.GetRepository<T>()).Returns(repositoryMock.Object);
            return parent;
        }
    }
}
