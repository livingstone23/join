using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;

namespace JOIN.Application.UseCases.Messaging.Tickets;

/// <summary>
/// Single point of truth that resolves the ticket-management capabilities of a user within a tenant
/// from the <see cref="TicketUserCompany"/> roster. Avoids re-implementing the
/// <c>GetAllAsync()</c> + LINQ lookup in every command handler.
/// </summary>
public sealed class TicketUserCompanyCapabilityResolver(IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Returns the capability flags of <paramref name="userId"/> within <paramref name="companyId"/>.
    /// All three flags are <c>false</c> when no active roster row exists for that pair
    /// or when the only matching row has <c>GcRecord != 0</c>.
    /// </summary>
    public async Task<TicketUserCompanyCapability> ResolveAsync(
        Guid userId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var repository = unitOfWork.GetRepository<TicketUserCompany>();
        var all = await repository.GetAllAsync();

        var row = all.FirstOrDefault(x =>
            x.GcRecord == 0
            && x.CompanyId == companyId
            && x.UserId == userId);

        return row is null
            ? default
            : new TicketUserCompanyCapability(
                row.IsSuperAdminTicket,
                row.CanFinishTicket,
                row.CanResolveTicket);
    }
}

/// <summary>
/// Projection of the three capability flags of a <c>TicketUserCompany</c> row,
/// returned by <see cref="TicketUserCompanyCapabilityResolver.ResolveAsync"/>.
/// </summary>
public readonly record struct TicketUserCompanyCapability(
    bool IsSuperAdminTicket,
    bool CanFinishTicket,
    bool CanResolveTicket);
