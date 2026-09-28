using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies;

/// <summary>
/// Unit tests for <see cref="TicketUserCompanySuperAdminCoordinator"/>.
/// </summary>
public sealed class TicketUserCompanySuperAdminCoordinatorTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task AnotherActiveSuperAdminExistsAsync_WhenNoRowsExist_ShouldReturnFalse()
    {
        var context = new CoordinatorTestContext();
        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketUserCompany>());

        var coordinator = new TicketUserCompanySuperAdminCoordinator(context.UnitOfWorkMock.Object);
        var result = await coordinator.AnotherActiveSuperAdminExistsAsync(_fixture.Create<Guid>(), _fixture.Create<Guid>(), CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task AnotherActiveSuperAdminExistsAsync_WhenOnlyExcludedRowIsSuperAdmin_ShouldReturnFalse()
    {
        var companyId = _fixture.Create<Guid>();
        var excluded = new TicketUserCompany { CompanyId = companyId, IsSuperAdminTicket = true, GcRecord = 0 };
        var context = new CoordinatorTestContext();
        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<TicketUserCompany> { excluded });

        var coordinator = new TicketUserCompanySuperAdminCoordinator(context.UnitOfWorkMock.Object);
        var result = await coordinator.AnotherActiveSuperAdminExistsAsync(companyId, excluded.Id, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task AnotherActiveSuperAdminExistsAsync_WhenAnotherActiveSuperAdminExists_ShouldReturnTrue()
    {
        var companyId = _fixture.Create<Guid>();
        var excluded = new TicketUserCompany { CompanyId = companyId, IsSuperAdminTicket = true, GcRecord = 0 };
        var other = new TicketUserCompany { CompanyId = companyId, IsSuperAdminTicket = true, GcRecord = 0 };
        var context = new CoordinatorTestContext();
        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<TicketUserCompany> { excluded, other });

        var coordinator = new TicketUserCompanySuperAdminCoordinator(context.UnitOfWorkMock.Object);
        var result = await coordinator.AnotherActiveSuperAdminExistsAsync(companyId, excluded.Id, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task AnotherActiveSuperAdminExistsAsync_WhenOtherSuperAdminIsSoftDeleted_ShouldReturnFalse()
    {
        var companyId = _fixture.Create<Guid>();
        var excluded = new TicketUserCompany { CompanyId = companyId, IsSuperAdminTicket = true, GcRecord = 0 };
        var softDeleted = new TicketUserCompany { CompanyId = companyId, IsSuperAdminTicket = true, GcRecord = 1 };
        var context = new CoordinatorTestContext();
        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<TicketUserCompany> { excluded, softDeleted });

        var coordinator = new TicketUserCompanySuperAdminCoordinator(context.UnitOfWorkMock.Object);
        var result = await coordinator.AnotherActiveSuperAdminExistsAsync(companyId, excluded.Id, CancellationToken.None);

        result.Should().BeFalse();
    }

    private sealed class CoordinatorTestContext
    {
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<TicketUserCompany>> RepositoryMock { get; } = new();

        public CoordinatorTestContext()
        {
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketUserCompany>()).Returns(RepositoryMock.Object);
        }
    }
}
