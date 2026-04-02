


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

}