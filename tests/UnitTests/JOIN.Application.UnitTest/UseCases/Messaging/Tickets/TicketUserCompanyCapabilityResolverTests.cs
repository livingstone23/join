using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets;

/// <summary>
/// Unit tests for <see cref="TicketUserCompanyCapabilityResolver"/>. The resolver is
/// the single point of truth that maps a user to their ticket-management capabilities
/// inside a tenant — every <c>CanResolveTicket</c> / <c>CanFinishTicket</c> /
/// <c>IsSuperAdminTicket</c> lookup in the ticket commands flows through it (SPEC 35 F2).
/// </summary>
public sealed class TicketUserCompanyCapabilityResolverTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that without any active <see cref="TicketUserCompany"/> row for the
    /// (user, company) pair, the resolver returns a default
    /// <see cref="TicketUserCompanyCapability"/> with all three flags <c>false</c>.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenNoActiveRosterRow_ShouldReturnAllFlagsFalse()
    {
        // Arrange
        var userId = _fixture.Create<Guid>();
        var companyId = _fixture.Create<Guid>();
        var context = new ResolverTestContext();
        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketUserCompany>());

        // Act
        var capability = await context.CreateResolver().ResolveAsync(userId, companyId, CancellationToken.None);

        // Assert
        capability.IsSuperAdminTicket.Should().BeFalse();
        capability.CanFinishTicket.Should().BeFalse();
        capability.CanResolveTicket.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that an active roster row returns the capability flags verbatim
    /// (no transformation, no masking) — including the case where all three flags
    /// are <c>true</c> on a single row.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenActiveRosterRowExists_ShouldReturnItsFlagsVerbatim()
    {
        // Arrange
        var userId = _fixture.Create<Guid>();
        var companyId = _fixture.Create<Guid>();
        var row = new TicketUserCompany
        {
            CompanyId = companyId,
            UserId = userId,
            IsSuperAdminTicket = true,
            CanFinishTicket = true,
            CanResolveTicket = true,
            GcRecord = 0
        };

        var context = new ResolverTestContext();
        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { row });

        // Act
        var capability = await context.CreateResolver().ResolveAsync(userId, companyId, CancellationToken.None);

        // Assert
        capability.IsSuperAdminTicket.Should().BeTrue();
        capability.CanFinishTicket.Should().BeTrue();
        capability.CanResolveTicket.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a row with <c>GcRecord != 0</c> (soft-deleted) is ignored —
    /// the resolver must not return its flags, otherwise a removed roster entry
    /// would keep granting capabilities indefinitely.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenOnlyRowIsSoftDeleted_ShouldReturnAllFlagsFalse()
    {
        // Arrange
        var userId = _fixture.Create<Guid>();
        var companyId = _fixture.Create<Guid>();
        var deletedRow = new TicketUserCompany
        {
            CompanyId = companyId,
            UserId = userId,
            IsSuperAdminTicket = true,
            CanFinishTicket = true,
            CanResolveTicket = true,
            GcRecord = 1
        };

        var context = new ResolverTestContext();
        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { deletedRow });

        // Act
        var capability = await context.CreateResolver().ResolveAsync(userId, companyId, CancellationToken.None);

        // Assert
        capability.IsSuperAdminTicket.Should().BeFalse();
        capability.CanFinishTicket.Should().BeFalse();
        capability.CanResolveTicket.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the cross-tenant guard: a roster row that matches the user id but
    /// belongs to a different company must not contribute flags — without this
    /// guard a user from tenant A could escalate privileges inside tenant B.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenRowBelongsToDifferentTenant_ShouldReturnAllFlagsFalse()
    {
        // Arrange
        var userId = _fixture.Create<Guid>();
        var companyId = _fixture.Create<Guid>();
        var otherCompanyId = _fixture.Create<Guid>();
        var foreignRow = new TicketUserCompany
        {
            CompanyId = otherCompanyId,
            UserId = userId,
            IsSuperAdminTicket = true,
            CanFinishTicket = true,
            CanResolveTicket = true,
            GcRecord = 0
        };

        var context = new ResolverTestContext();
        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { foreignRow });

        // Act
        var capability = await context.CreateResolver().ResolveAsync(userId, companyId, CancellationToken.None);

        // Assert
        capability.IsSuperAdminTicket.Should().BeFalse();
        capability.CanFinishTicket.Should().BeFalse();
        capability.CanResolveTicket.Should().BeFalse();
    }

    private sealed class ResolverTestContext
    {
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<TicketUserCompany>> RepositoryMock { get; } = new();

        public ResolverTestContext()
        {
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketUserCompany>()).Returns(RepositoryMock.Object);
        }

        public TicketUserCompanyCapabilityResolver CreateResolver() => new(UnitOfWorkMock.Object);
    }
}