using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;

namespace JOIN.Application.UseCases.Messaging.Tickets;

/// <summary>
/// Enforces the optional per-company ticket status transition rules (SPEC 37).
/// Opt-in per source status: if no rules are configured for <paramref name="fromStatusId"/>,
/// every destination is allowed (same behavior as before this guard). Once a company
/// configures at least one rule for a source status, the destination set is restricted
/// to the explicitly listed <c>ToStatusId</c> values.
/// </summary>
public sealed class TicketStatusTransitionGuard(IUnitOfWork unitOfWork)
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Returns <c>true</c> when the transition is permitted by the current tenant's rules,
    /// or when no rules are configured for the source status.
    /// </summary>
    /// <param name="companyId">Tenant identifier.</param>
    /// <param name="fromStatusId">Source <c>TicketStatus</c> identifier.</param>
    /// <param name="toStatusId">Destination <c>TicketStatus</c> identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task<bool> IsAllowedAsync(
        Guid companyId,
        Guid fromStatusId,
        Guid toStatusId,
        CancellationToken cancellationToken)
    {
        // Not a real transition — skip the repository roundtrip.
        if (fromStatusId == toStatusId)
        {
            return true;
        }

        var repository = _unitOfWork.GetRepository<TicketStatusTransition>();
        var all = await repository.GetAllAsync();

        var rulesForFromStatus = all
            .Where(x => x.GcRecord == 0 && x.CompanyId == companyId && x.FromStatusId == fromStatusId)
            .ToList();

        // No rules configured for this source status: unrestricted, same as today.
        if (rulesForFromStatus.Count == 0)
        {
            return true;
        }

        return rulesForFromStatus.Any(x => x.ToStatusId == toStatusId);
    }
}