using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies;

/// <summary>
/// Enforces the "at least one active <c>IsSuperAdminTicket</c> per tenant" invariant
/// across the <see cref="TicketUserCompany"/> roster.
/// </summary>
public sealed class TicketUserCompanySuperAdminCoordinator(IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Returns <c>true</c> when at least one OTHER active row with
    /// <c>IsSuperAdminTicket = true</c> exists for the same tenant.
    /// Soft-deleted rows and the excluded row are ignored.
    /// </summary>
    public async Task<bool> AnotherActiveSuperAdminExistsAsync(
        Guid companyId,
        Guid excludeId,
        CancellationToken cancellationToken)
    {
        var repository = unitOfWork.GetRepository<TicketUserCompany>();
        var all = await repository.GetAllAsync();

        return all.Any(x =>
            x.GcRecord == 0
            && x.CompanyId == companyId
            && x.Id != excludeId
            && x.IsSuperAdminTicket);
    }
}
