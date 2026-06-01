using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;



namespace JOIN.Application.Interface.Persistence.Admin;



/// <summary>
/// Defines the data access contract for <see cref="PersonEmployment"/> write-side queries
/// used to enforce the single-current-employment rule per person within a tenant.
/// </summary>
public interface IPersonEmploymentRepository : IGenericRepository<PersonEmployment>
{
    /// <summary>
    /// Retrieves active employments for a person that are currently marked as current.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="personId">The person identifier.</param>
    /// <param name="excludeEmploymentId">
    /// Optional employment identifier to exclude (for example, the employment being updated).
    /// </param>
    /// <param name="cancellationToken">A cancellation token for the query.</param>
    /// <returns>
    /// Active employments with <see cref="PersonEmployment.IsCurrent"/> set to <c>true</c>,
    /// scoped to the given company and person.
    /// </returns>
    Task<IReadOnlyList<PersonEmployment>> GetActiveCurrentAsync(
        Guid companyId,
        Guid personId,
        Guid? excludeEmploymentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recently created active employment for a person,
    /// excluding a specific employment when provided.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="personId">The person identifier.</param>
    /// <param name="excludeEmploymentId">The employment identifier to exclude from the result.</param>
    /// <param name="cancellationToken">A cancellation token for the query.</param>
    /// <returns>
    /// The active employment with the latest <see cref="BaseAuditableEntity.Created"/> timestamp,
    /// or <c>null</c> if no other active employment exists.
    /// </returns>
    Task<PersonEmployment?> GetMostRecentActiveAsync(
        Guid companyId,
        Guid personId,
        Guid excludeEmploymentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an active, non-deleted employment by identifier scoped to the tenant.
    /// </summary>
    /// <param name="id">The employment identifier.</param>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the query.</param>
    /// <returns>The matching employment, or <c>null</c> if not found.</returns>
    Task<PersonEmployment?> GetActiveByIdAsync(
        Guid id,
        Guid companyId,
        CancellationToken cancellationToken = default);
}
