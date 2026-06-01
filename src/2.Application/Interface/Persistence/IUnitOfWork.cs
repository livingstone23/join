using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Application.Interface.Persistence.Security;

namespace JOIN.Application.Interface.Persistence;

/// <summary>
/// Represents the Unit of Work pattern to coordinate the writing of multiple repositories,
/// ensuring all database changes are committed within a single, atomic transaction.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    
    // --- 1. Custom Shortcuts ---
    // Keep this for repositories that have custom logic (non-generic)
    IPersonsRepository Persons { get; }
    IPersonAddressRepository PersonAddresses { get; }
    IPersonContactRepository PersonContacts { get; }
    IPersonEmploymentRepository PersonEmployments { get; }
    IPersonBusinessProfileRepository PersonBusinessProfiles { get; }
    IPersonFinancialProfileRepository PersonFinancialProfiles { get; }
    IRoleSystemOptionsRepository RoleSystemOptions { get; }

    // --- 2. Scalable Access ---
    // This allows access to any entity without modifying this interface ever again
    IGenericRepository<TEntity> GetRepository<TEntity>() where TEntity : class;

    // --- 3. Atomic Transaction ---
    Task<int> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists all tracked changes in a single atomic transaction.
    /// </summary>
    /// <param name="cancellationToken">Token to observe while waiting for the task to complete.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

}