


using JOIN.Domain.Admin;



namespace JOIN.Application.Interface.Persistence.Admin;



/// <summary>
/// Defines the specific data access contract for the Customer domain entity.
/// Extends the generic repository to include any domain-specific query methods.
/// </summary>
public interface ICustomersRepository : IGenericRepository<Customer>
{

    // Note: Add specific query contracts here in the future.
    // Example: Task<Customer?> GetByVatNumberAsync(string vatNumber);

}