


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

}