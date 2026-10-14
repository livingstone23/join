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
        // Same lookup as ResolveWithMembershipAsync, so GET /TicketUserCompanies/me (SPEC 53)
        // and the commands that enforce these flags can never disagree.
        var capability = await ResolveWithMembershipAsync(userId, companyId, cancellationToken);

        return new TicketUserCompanyCapability(
            capability.IsSuperAdminTicket,
            capability.CanFinishTicket,
            capability.CanResolveTicket);
    }

    /// <summary>
    /// Returns the capability flags plus <see cref="TicketUserCompanyCapability.IsMember"/>,
    /// which is <c>true</c> exactly when the caller has an active row on the roster.
    /// Used by <c>GET /TicketUserCompanies/me</c> (SPEC 53) so the front can render the
    /// right buttons even when the caller is not on the roster (in which case
    /// <see cref="TicketUserCompanyCapability"/> already returns all flags <c>false</c>).
    /// </summary>
    public async Task<TicketUserCompanyCapabilityWithMembership> ResolveWithMembershipAsync(
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
            : new TicketUserCompanyCapabilityWithMembership(
                IsMember: true,
                IsSuperAdminTicket: row.IsSuperAdminTicket,
                CanFinishTicket: row.CanFinishTicket,
                CanResolveTicket: row.CanResolveTicket);
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

/// <summary>
/// Same three capability flags plus an <see cref="IsMember"/> marker. Spec
/// <c>GET /TicketUserCompanies/me</c> (SPEC 53) needs <see cref="IsMember"/>
/// to distinguish "caller is on the roster, just not enabled" from
/// "caller is not on the roster at all" without changing
/// <see cref="TicketUserCompanyCapability"/> for existing callers.
/// </summary>
public readonly record struct TicketUserCompanyCapabilityWithMembership(
    bool IsMember,
    bool IsSuperAdminTicket,
    bool CanFinishTicket,
    bool CanResolveTicket);
