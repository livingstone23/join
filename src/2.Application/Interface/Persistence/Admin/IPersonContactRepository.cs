using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;



namespace JOIN.Application.Interface.Persistence.Admin;



/// <summary>
/// Defines the data access contract for <see cref="PersonContact"/> write-side queries
/// used to enforce the single-primary-contact rule per <see cref="ContactType"/> within a tenant.
/// </summary>
public interface IPersonContactRepository : IGenericRepository<PersonContact>
{
    /// <summary>
    /// Retrieves active contacts for a person that are currently marked as primary for the given type.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="personId">The person identifier.</param>
    /// <param name="contactType">The contact category.</param>
    /// <param name="excludeContactId">
    /// Optional contact identifier to exclude (for example, the contact being updated).
    /// </param>
    /// <param name="cancellationToken">A cancellation token for the query.</param>
    /// <returns>
    /// Active contacts with <see cref="PersonContact.IsPrimary"/> set to <c>true</c>,
    /// scoped to the given company, person, and contact type.
    /// </returns>
    Task<IReadOnlyList<PersonContact>> GetActiveWithPrimaryByTypeAsync(
        Guid companyId,
        Guid personId,
        ContactType contactType,
        Guid? excludeContactId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recently created active contact for a person and type,
    /// excluding a specific contact when provided.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="personId">The person identifier.</param>
    /// <param name="contactType">The contact category.</param>
    /// <param name="excludeContactId">The contact identifier to exclude from the result.</param>
    /// <param name="cancellationToken">A cancellation token for the query.</param>
    /// <returns>
    /// The active contact with the latest <see cref="BaseAuditableEntity.Created"/> timestamp,
    /// or <c>null</c> if no other active contact exists for that type.
    /// </returns>
    Task<PersonContact?> GetMostRecentActiveByTypeAsync(
        Guid companyId,
        Guid personId,
        ContactType contactType,
        Guid excludeContactId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an active, non-deleted contact by identifier scoped to the tenant.
    /// </summary>
    /// <param name="id">The contact identifier.</param>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the query.</param>
    /// <returns>The matching contact, or <c>null</c> if not found.</returns>
    Task<PersonContact?> GetActiveByIdAsync(
        Guid id,
        Guid companyId,
        CancellationToken cancellationToken = default);
}
