using JOIN.Application.Interface.Persistence.Customers;
using JOIN.Domain.Customers;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Persistence.Repositories.Customers;

/// <summary>
/// EF Core repository for <see cref="Customer"/> (SPEC 55).
/// </summary>
public sealed class CustomerRepository(ApplicationDbContext context)
    : GenericRepository<Customer>(context), ICustomerRepository
{
    /// <inheritdoc />
    public Task<Customer?> GetByPersonAsync(Guid companyId, Guid personId, CancellationToken cancellationToken = default) =>
        _context.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.PersonId == personId, cancellationToken);

    /// <inheritdoc />
    public Task<Customer?> GetByIdAsync(Guid companyId, Guid customerId, CancellationToken cancellationToken = default) =>
        _context.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Id == customerId, cancellationToken);
}
