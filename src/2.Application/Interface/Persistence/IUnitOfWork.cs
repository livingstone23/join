using JOIN.Application.Interface.Persistence.Admin;

namespace JOIN.Application.Interface.Persistence;

/// <summary>
/// Represents the Unit of Work pattern to coordinate the writing of multiple repositories,
/// ensuring all database changes are committed within a single, atomic transaction.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    
    /// <summary>
    /// Gets the customer repository instance.
    /// </summary>
    ICustomersRepository Customers { get; }

    /// <summary>
    /// Commits all tracked changes to the underlying database.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that operations should be canceled.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveAsync(CancellationToken cancellationToken = default);
    
}