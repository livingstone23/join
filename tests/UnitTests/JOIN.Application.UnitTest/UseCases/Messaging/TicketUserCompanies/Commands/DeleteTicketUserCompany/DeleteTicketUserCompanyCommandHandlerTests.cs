using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.DeleteTicketUserCompany;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies.Commands.DeleteTicketUserCompany;

/// <summary>
/// Unit tests for <see cref="DeleteTicketUserCompanyCommandHandler"/>:
/// tenant guard, lookup, the "last IsSuperAdminTicket" invariant, and soft delete.
/// </summary>
public sealed class DeleteTicketUserCompanyCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new DeleteTicketUserCompanyCommandTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        var response = await handler.Handle(new DeleteTicketUserCompanyCommand(_fixture.Create<Guid>()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenRowDoesNotExist_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteTicketUserCompanyCommandTestContext(companyId);
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync((TicketUserCompany?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new DeleteTicketUserCompanyCommand(_fixture.Create<Guid>()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_USER_COMPANY_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenDeletingLastSuperAdmin_ShouldReturnLastSuperAdminError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteTicketUserCompanyCommandTestContext(companyId);
        var target = new TicketUserCompany { CompanyId = companyId, IsSuperAdminTicket = true, GcRecord = 0 };
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAsync(target.Id)).ReturnsAsync(target);
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
            new List<TicketUserCompany> { target });

        var handler = context.CreateHandler();
        var response = await handler.Handle(new DeleteTicketUserCompanyCommand(target.Id), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("LAST_SUPERADMIN_TICKET");
        context.TicketUserCompanyRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TicketUserCompany>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAnotherSuperAdminActive_ShouldSoftDelete()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteTicketUserCompanyCommandTestContext(companyId);
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
        var response = await handler.Handle(new DeleteTicketUserCompanyCommand(target.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        updated.Should().NotBeNull();
        updated!.GcRecord.Should().NotBe(0, "MarkAsDeleted sets GcRecord to a non-zero stamp");
        response.Data.Should().Be(target.Id);
    }

    private sealed class DeleteTicketUserCompanyCommandTestContext
    {
        public DeleteTicketUserCompanyCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            TicketUserCompanyRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TicketUserCompany>())).ReturnsAsync(true);
            SetupRepository(UnitOfWorkMock, TicketUserCompanyRepositoryMock);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketUserCompany>> TicketUserCompanyRepositoryMock { get; } = new();

        public DeleteTicketUserCompanyCommandHandler CreateHandler()
        {
            var coordinator = new TicketUserCompanySuperAdminCoordinator(UnitOfWorkMock.Object);
            return new DeleteTicketUserCompanyCommandHandler(UnitOfWorkMock.Object, CurrentUserServiceMock.Object, coordinator);
        }

        private static void SetupRepository<TEntity>(Mock<IUnitOfWork> unitOfWorkMock, Mock<IGenericRepository<TEntity>> repositoryMock)
            where TEntity : class
        {
            unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
        }
    }
}
