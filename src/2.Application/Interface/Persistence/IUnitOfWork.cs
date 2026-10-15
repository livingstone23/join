using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.Interface.Persistence.Customers;
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
    ICompanyModuleRepository CompanyModules { get; }
    IUserCompanyRepository UserCompanies { get; }
    ICalendarEventRepository CalendarEvents { get; }
    ICustomerRepository Customers { get; }

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

    // --- 4. Explicit Transaction Control ---
    // Intended to be invoked only by TransactionBehavior. Handlers must not call these directly.
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);

    // --- 5. Calendar schedule lock (SPEC 44) ---

    /// <summary>
    /// Takes a transaction-scoped application lock on <c>(companyId, ownerUserId)</c> so two concurrent commands
    /// cannot both pass the overlap check for the same user. Released when the transaction commits or rolls back
    /// (PostgreSQL <c>pg_advisory_xact_lock</c>). Must run inside the command's transaction.
    /// </summary>
    Task AcquireScheduleLockAsync(Guid companyId, Guid ownerUserId, CancellationToken cancellationToken = default);

    // --- 6. Customer per person lock (SPEC 55) ---

    /// <summary>
    /// Takes a transaction-scoped lock on <c>(companyId, personId)</c> so two concurrent events cannot both try to
    /// create the customer of the same person (PostgreSQL <c>pg_advisory_xact_lock</c>). Must run inside the
    /// command's transaction; released when it commits or rolls back.
    /// </summary>
    Task AcquireCustomerPersonLockAsync(Guid companyId, Guid personId, CancellationToken cancellationToken = default);

}