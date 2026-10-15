using JOIN.Domain.Customers;

namespace JOIN.Application.Interface.Persistence.Customers;

/// <summary>
/// EF Core repository for <see cref="Customer"/> with the tracked reads the customer commands need (SPEC 55).
/// Every method bypasses the global query filters and scopes by the arguments it receives.
/// </summary>
public interface ICustomerRepository : IGenericRepository<Customer>
{
    /// <summary>
    /// Returns the tracked customer of <paramref name="personId"/> in <paramref name="companyId"/>, live or not
    /// (there is at most one, <c>UX_Customers_Company_Person</c>), or <c>null</c>.
    /// </summary>
    Task<Customer?> GetByPersonAsync(Guid companyId, Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the tracked customer <paramref name="customerId"/> of <paramref name="companyId"/>, or <c>null</c>.
    /// </summary>
    Task<Customer?> GetByIdAsync(Guid companyId, Guid customerId, CancellationToken cancellationToken = default);
}
