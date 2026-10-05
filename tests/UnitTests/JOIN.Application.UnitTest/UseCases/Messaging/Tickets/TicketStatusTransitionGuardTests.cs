using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets;

/// <summary>
/// Unit tests for <see cref="TicketStatusTransitionGuard"/>. The guard is opt-in per
/// source status: with no rules configured for <c>FromStatusId</c>, every destination
/// is allowed (same behavior as before SPEC 37). Once a company configures at least
/// one rule for a source status, the destination set is restricted to the explicitly
/// listed <c>ToStatusId</c> values (SPEC 37 F5).
/// </summary>
public sealed class TicketStatusTransitionGuardTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IGenericRepository<TicketStatusTransition>> _repositoryMock = new();

    public TicketStatusTransitionGuardTests()
    {
        _unitOfWorkMock.Setup(x => x.GetRepository<TicketStatusTransition>()).Returns(_repositoryMock.Object);
    }

    [Fact]
    public async Task IsAllowedAsync_WhenNoRulesForFromStatus_ShouldReturnTrue()
    {
        var companyId = Guid.NewGuid();
        var fromStatusId = Guid.NewGuid();
        var toStatusId = Guid.NewGuid();

        _repositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<TicketStatusTransition>());

        var result = await new TicketStatusTransitionGuard(_unitOfWorkMock.Object)
            .IsAllowedAsync(companyId, fromStatusId, toStatusId, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAllowedAsync_WhenRulesExistButDestinationNotListed_ShouldReturnFalse()
    {
        var companyId = Guid.NewGuid();
        var fromStatusId = Guid.NewGuid();
        var allowedTo = Guid.NewGuid();
        var attemptedTo = Guid.NewGuid();

        _repositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new TicketStatusTransition { CompanyId = companyId, FromStatusId = fromStatusId, ToStatusId = allowedTo }
            });

        var result = await new TicketStatusTransitionGuard(_unitOfWorkMock.Object)
            .IsAllowedAsync(companyId, fromStatusId, attemptedTo, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsAllowedAsync_WhenRulesExistAndDestinationListed_ShouldReturnTrue()
    {
        var companyId = Guid.NewGuid();
        var fromStatusId = Guid.NewGuid();
        var toStatusId = Guid.NewGuid();

        _repositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new TicketStatusTransition { CompanyId = companyId, FromStatusId = fromStatusId, ToStatusId = toStatusId }
            });

        var result = await new TicketStatusTransitionGuard(_unitOfWorkMock.Object)
            .IsAllowedAsync(companyId, fromStatusId, toStatusId, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAllowedAsync_WhenFromEqualsTo_ShouldReturnTrueWithoutTouchingRepository()
    {
        var statusId = Guid.NewGuid();

        var result = await new TicketStatusTransitionGuard(_unitOfWorkMock.Object)
            .IsAllowedAsync(Guid.NewGuid(), statusId, statusId, CancellationToken.None);

        result.Should().BeTrue();
        _repositoryMock.Verify(x => x.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task IsAllowedAsync_WhenRulesBelongToOtherCompany_ShouldIgnoreThem()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var fromStatusId = Guid.NewGuid();
        var toStatusId = Guid.NewGuid();

        _repositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new TicketStatusTransition { CompanyId = otherCompanyId, FromStatusId = fromStatusId, ToStatusId = toStatusId }
            });

        var result = await new TicketStatusTransitionGuard(_unitOfWorkMock.Object)
            .IsAllowedAsync(companyId, fromStatusId, toStatusId, CancellationToken.None);

        result.Should().BeTrue();
    }
}