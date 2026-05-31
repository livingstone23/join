using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;



namespace JOIN.Application.Interface.Persistence.Admin;



/// <summary>
/// Defines the data access contract for <see cref="PersonAddress"/> write-side queries
/// used to enforce the single-default-address rule per person within a tenant.
/// </summary>
public interface IPersonAddressRepository : IGenericRepository<PersonAddress>
{
    /// <summary>
    /// Retrieves active addresses for a person that are currently marked as default.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="personId">The person identifier.</param>
    /// <param name="excludeAddressId">
    /// Optional address identifier to exclude (for example, the address being updated).
    /// </param>
    /// <param name="cancellationToken">A cancellation token for the query.</param>
    /// <returns>
    /// Active addresses with <see cref="PersonAddress.IsDefault"/> set to <c>true</c>,
    /// scoped to the given company and person.
    /// </returns>
    Task<IReadOnlyList<PersonAddress>> GetActiveWithDefaultAsync(
        Guid companyId,
        Guid personId,
        Guid? excludeAddressId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recently created active address for a person,
    /// excluding a specific address when provided.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="personId">The person identifier.</param>
    /// <param name="excludeAddressId">The address identifier to exclude from the result.</param>
    /// <param name="cancellationToken">A cancellation token for the query.</param>
    /// <returns>
    /// The active address with the latest <see cref="BaseAuditableEntity.Created"/> timestamp,
    /// or <c>null</c> if no other active address exists.
    /// </returns>
    Task<PersonAddress?> GetMostRecentActiveAsync(
        Guid companyId,
        Guid personId,
        Guid excludeAddressId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an active, non-deleted address by identifier scoped to the tenant.
    /// </summary>
    /// <param name="id">The address identifier.</param>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the query.</param>
    /// <returns>The matching address, or <c>null</c> if not found.</returns>
    Task<PersonAddress?> GetActiveByIdAsync(
        Guid id,
        Guid companyId,
        CancellationToken cancellationToken = default);
}
