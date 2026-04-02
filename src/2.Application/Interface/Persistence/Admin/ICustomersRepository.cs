


using JOIN.Domain.Admin;



namespace JOIN.Application.Interface.Persistence.Admin;



/// <summary>
/// Defines the specific data access contract for the Customer domain entity.
/// Extends the generic repository to include any domain-specific query methods.
/// </summary>
public interface ICustomersRepository : IGenericRepository<Customer>
{
    /// <summary>
    /// Retrieves a customer by ID including the linked IdentificationType navigation.
    /// </summary>
    Task<Customer?> GetByIdWithIdentificationTypeAsync(Guid id);

    /// <summary>
    /// Determines whether an active customer exists for the given company and identification number.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="identificationNumber">The customer identification number.</param>
    /// <returns><c>true</c> if a matching active customer exists; otherwise, <c>false</c>.</returns>
    Task<bool> ExistsByCompanyAndIdentificationAsync(Guid companyId, string identificationNumber);

    /// <summary>
    /// Determines whether an active customer exists for the given company and identification pair,
    /// excluding the specified customer id.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="customerId">The customer identifier to exclude from the check.</param>
    /// <param name="identificationTypeId">The identification type identifier.</param>
    /// <param name="identificationNumber">The customer identification number.</param>
    /// <returns><c>true</c> if a different active customer uses the same identification pair; otherwise, <c>false</c>.</returns>
    Task<bool> ExistsByCompanyAndIdentificationExceptIdAsync(
        Guid companyId,
        Guid customerId,
        Guid identificationTypeId,
        string identificationNumber);

    /// <summary>
    /// Retrieves a customer aggregate for update with addresses and contacts loaded.
    /// </summary>
    /// <param name="id">The customer identifier.</param>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <returns>The tracked customer aggregate, or <c>null</c> if not found.</returns>
    Task<Customer?> GetForUpdateAsync(Guid id, Guid companyId);

}