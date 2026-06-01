using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;



namespace JOIN.Application.Interface.Persistence.Admin;



/// <summary>
/// Defines the data access contract for <see cref="PersonFinancialProfile"/> write-side queries
/// used to enforce the single-current-financial-profile rule per person within a tenant.
/// </summary>
public interface IPersonFinancialProfileRepository : IGenericRepository<PersonFinancialProfile>
{
    /// <summary>
    /// Retrieves financial profiles for a person that are currently marked as current and active.
    /// </summary>
    Task<IReadOnlyList<PersonFinancialProfile>> GetActiveCurrentAsync(
        Guid companyId,
        Guid personId,
        Guid? excludeProfileId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recent active financial profile for a person,
    /// ordered by <see cref="PersonFinancialProfile.DeclaredDate"/> then <see cref="BaseAuditableEntity.Created"/>.
    /// </summary>
    Task<PersonFinancialProfile?> GetMostRecentActiveAsync(
        Guid companyId,
        Guid personId,
        Guid excludeProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a non-deleted financial profile by identifier scoped to the tenant.
    /// </summary>
    Task<PersonFinancialProfile?> GetActiveByIdAsync(
        Guid id,
        Guid companyId,
        CancellationToken cancellationToken = default);
}
