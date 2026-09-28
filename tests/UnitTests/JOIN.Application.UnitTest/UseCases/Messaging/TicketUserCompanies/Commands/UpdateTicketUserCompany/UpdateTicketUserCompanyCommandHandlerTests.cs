using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.UpdateTicketUserCompany;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies.Commands.UpdateTicketUserCompany;

/// <summary>
/// Unit tests for <see cref="UpdateTicketUserCompanyCommandHandler"/>:
/// tenant guard, lookup, the "last IsSuperAdminTicket" invariant, and flag updates.
/// </summary>
public sealed class UpdateTicketUserCompanyCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new UpdateTicketUserCompanyCommandTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenRowDoesNotExist_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateTicketUserCompanyCommandTestContext(companyId);
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync((TicketUserCompany?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_USER_COMPANY_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenRowBelongsToDifferentTenant_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateTicketUserCompanyCommandTestContext(companyId);
        var otherTenantRow = new TicketUserCompany { CompanyId = _fixture.Create<Guid>(), IsSuperAdminTicket = true, GcRecord = 0 };
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAsync(otherTenantRow.Id)).ReturnsAsync(otherTenantRow);

        var handler = context.CreateHandler();
        var response = await handler.Handle(CreateValidCommand(id: otherTenantRow.Id, isSuperAdminTicket: false), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_USER_COMPANY_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenDisablingLastSuperAdmin_ShouldReturnLastSuperAdminError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateTicketUserCompanyCommandTestContext(companyId);
        var target = new TicketUserCompany { CompanyId = companyId, IsSuperAdminTicket = true, GcRecord = 0 };
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAsync(target.Id)).ReturnsAsync(target);
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
            new List<TicketUserCompany> { target });

        var handler = context.CreateHandler();
        var response = await handler.Handle(CreateValidCommand(id: target.Id, isSuperAdminTicket: false), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("LAST_SUPERADMIN_TICKET");
        context.TicketUserCompanyRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TicketUserCompany>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenDisablingSuperAdminWithAnotherActive_ShouldPersist()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateTicketUserCompanyCommandTestContext(companyId);
        var target = new TicketUserCompany { CompanyId = companyId, IsSuperAdminTicket = true, GcRecord = 0 };
        var other = new TicketUserCompany { CompanyId = companyId, IsSuperAdminTicket = true, GcRecord = 0 };
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAsync(target.Id)).ReturnsAsync(target);
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
            new List<TicketUserCompany> { target, other });

        TicketUserCompany? updated = null;
        context.TicketUserCompanyRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TicketUserCompany>()))
            .Callback<TicketUserCompany>(entity => updated = entity)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();
        var response = await handler.Handle(CreateValidCommand(id: target.Id, isSuperAdminTicket: false), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        updated.Should().NotBeNull();
        updated!.IsSuperAdminTicket.Should().BeFalse();
        updated.LastModified.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_WhenEnablingSuperAdmin_ShouldNotCallCoordinator()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new UpdateTicketUserCompanyCommandTestContext(companyId);
        var target = new TicketUserCompany { CompanyId = companyId, IsSuperAdminTicket = false, GcRecord = 0 };
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAsync(target.Id)).ReturnsAsync(target);
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketUserCompany>());

        TicketUserCompany? updated = null;
        context.TicketUserCompanyRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TicketUserCompany>()))
            .Callback<TicketUserCompany>(entity => updated = entity)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();
        var response = await handler.Handle(CreateValidCommand(id: target.Id, isSuperAdminTicket: true), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        updated!.IsSuperAdminTicket.Should().BeTrue();
    }

    private UpdateTicketUserCompanyCommand CreateValidCommand(
        Guid? id = null,
        bool isSuperAdminTicket = true,
        bool canFinishTicket = true,
        bool canResolveTicket = true)
    {
        return new UpdateTicketUserCompanyCommand
        {
            Id = id ?? _fixture.Create<Guid>(),
            IsSuperAdminTicket = isSuperAdminTicket,
            CanFinishTicket = canFinishTicket,
            CanResolveTicket = canResolveTicket
        };
    }

    private sealed class UpdateTicketUserCompanyCommandTestContext
    {
        public UpdateTicketUserCompanyCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            TicketUserCompanyRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketUserCompany>())).ReturnsAsync(true);
            TicketUserCompanyRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TicketUserCompany>())).ReturnsAsync(true);
            UserRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync((ApplicationUser?)null);
            CompanyRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync((Company?)null);
            SetupRepository(UnitOfWorkMock, TicketUserCompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserRepositoryMock);
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketUserCompany>> TicketUserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();

        public UpdateTicketUserCompanyCommandHandler CreateHandler()
        {
            var coordinator = new TicketUserCompanySuperAdminCoordinator(UnitOfWorkMock.Object);
            return new UpdateTicketUserCompanyCommandHandler(UnitOfWorkMock.Object, CurrentUserServiceMock.Object, coordinator);
        }

        private static void SetupRepository<TEntity>(Mock<IUnitOfWork> unitOfWorkMock, Mock<IGenericRepository<TEntity>> repositoryMock)
            where TEntity : class
        {
            unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
        }
    }
}
