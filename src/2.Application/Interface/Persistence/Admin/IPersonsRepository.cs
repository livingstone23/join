


using JOIN.Domain.Admin;



namespace JOIN.Application.Interface.Persistence.Admin;



/// <summary>
/// Defines the specific data access contract for the Person domain entity.
/// Extends the generic repository to include any domain-specific query methods.
/// </summary>
public interface IPersonsRepository : IGenericRepository<Person>
{


    /// <summary>
    /// Retrieves a customer by ID including the linked IdentificationType navigation.
    /// </summary>
    Task<Person?> GetByIdWithIdentificationTypeAsync(Guid id);

    /// <summary>
    /// Determines whether an active person exists for the given company and identification number.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="identificationNumber">The person identification number.</param>
    /// <returns><c>true</c> if a matching active person exists; otherwise, <c>false</c>.</returns>
    Task<bool> ExistsByCompanyAndIdentificationAsync(Guid companyId, string identificationNumber);

    /// <summary>
    /// Determines whether an active person exists for the given company and identification pair,
    /// excluding the specified person id.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="personId">The person identifier to exclude from the check.</param>
    /// <param name="identificationTypeId">The identification type identifier.</param>
    /// <param name="identificationNumber">The person identification number.</param>
    /// <returns><c>true</c> if a different active person uses the same identification pair; otherwise, <c>false</c>.</returns>
    Task<bool> ExistsByCompanyAndIdentificationExceptIdAsync(
        Guid companyId,
        Guid personId,
        Guid identificationTypeId,
        string identificationNumber);

    /// <summary>
    /// Retrieves a person aggregate for update with addresses and contacts loaded.
    /// </summary>
    /// <param name="id">The person identifier.</param>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <returns>The tracked person aggregate, or <c>null</c> if not found.</returns>
    Task<Person?> GetForUpdateAsync(Guid id, Guid companyId);
    
}