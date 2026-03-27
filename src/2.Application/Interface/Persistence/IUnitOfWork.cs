using JOIN.Application.Interface.Persistence.Admin;

namespace JOIN.Application.Interface.Persistence;

/// <summary>
/// Represents the Unit of Work pattern to coordinate the writing of multiple repositories,
/// ensuring all database changes are committed within a single, atomic transaction.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    
    // --- 1. Custom Shortcuts ---
    // Keep this for repositories that have custom logic (non-generic)
    ICustomersRepository Customers { get; }

    // --- 2. Scalable Access ---
    // This allows access to any entity without modifying this interface ever again
    IGenericRepository<TEntity> GetRepository<TEntity>() where TEntity : class;

    // --- 3. Atomic Transaction ---
    Task<int> SaveAsync(CancellationToken cancellationToken = default);

}