using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;



namespace JOIN.Application.Interface.Persistence.Admin;



/// <summary>
/// Defines the data access contract for <see cref="PersonBusinessProfile"/> write-side queries
/// used to enforce the single-active-business-profile rule per person within a tenant.
/// </summary>
public interface IPersonBusinessProfileRepository : IGenericRepository<PersonBusinessProfile>
{
    /// <summary>
    /// Retrieves business profiles for a person that are currently active
    /// (<see cref="PersonBusinessProfile.IsActive"/> and not soft-deleted).
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="personId">The person identifier.</param>
    /// <param name="excludeProfileId">
    /// Optional profile identifier to exclude (for example, the profile being reactivated).
    /// </param>
    /// <param name="cancellationToken">A cancellation token for the query.</param>
    /// <returns>
    /// Profiles matching <see cref="PersonBusinessProfile.IsCurrentlyActive"/>,
    /// scoped to the given company and person. Normally 0-1; may return more if legacy data exists.
    /// </returns>
    Task<IReadOnlyList<PersonBusinessProfile>> GetActiveProfilesAsync(
        Guid companyId,
        Guid personId,
        Guid? excludeProfileId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a non-deleted business profile by identifier scoped to the tenant
    /// (includes inactive profiles so reactivation via Update is supported).
    /// </summary>
    /// <param name="id">The business profile identifier.</param>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the query.</param>
    /// <returns>The matching profile, or <c>null</c> if not found.</returns>
    Task<PersonBusinessProfile?> GetActiveByIdAsync(
        Guid id,
        Guid companyId,
        CancellationToken cancellationToken = default);
}
