using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Application.Interface.Persistence.Customers;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Persistence.Repositories;

namespace JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;

/// <summary>
/// Real <see cref="UnitOfWork"/> over an InMemory context, without the PostgreSQL schedule lock
/// (<c>pg_advisory_xact_lock</c> needs a relational provider) and without explicit transactions.
/// Counts the lock requests so the tests can assert that the overlap check took it.
/// </summary>
internal sealed class LockFreeUnitOfWork(UnitOfWork inner) : IUnitOfWork
{
    public int LockRequests { get; private set; }

    /// <summary> Customer-per-person lock requests (SPEC 55). </summary>
    public int CustomerLockRequests { get; private set; }

    /// <summary> When set, the next schedule lock throws it (simulates a database error raised mid-command). </summary>
    public Exception? ScheduleLockFailure { get; set; }

    public IPersonsRepository Persons => inner.Persons;
    public IPersonAddressRepository PersonAddresses => inner.PersonAddresses;
    public IPersonContactRepository PersonContacts => inner.PersonContacts;
    public IPersonEmploymentRepository PersonEmployments => inner.PersonEmployments;
    public IPersonBusinessProfileRepository PersonBusinessProfiles => inner.PersonBusinessProfiles;
    public IPersonFinancialProfileRepository PersonFinancialProfiles => inner.PersonFinancialProfiles;
    public IRoleSystemOptionsRepository RoleSystemOptions => inner.RoleSystemOptions;
    public ICompanyModuleRepository CompanyModules => inner.CompanyModules;
    public IUserCompanyRepository UserCompanies => inner.UserCompanies;
    public ICalendarEventRepository CalendarEvents => inner.CalendarEvents;
    public ICustomerRepository Customers => inner.Customers;

    public IGenericRepository<TEntity> GetRepository<TEntity>() where TEntity : class => inner.GetRepository<TEntity>();
    public Task<int> SaveAsync(CancellationToken cancellationToken = default) => inner.SaveAsync(cancellationToken);
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => inner.SaveChangesAsync(cancellationToken);
    public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AcquireScheduleLockAsync(Guid companyId, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        LockRequests++;
        if (ScheduleLockFailure is { } failure)
        {
            ScheduleLockFailure = null;
            throw failure;
        }

        return Task.CompletedTask;
    }

    public Task AcquireCustomerPersonLockAsync(Guid companyId, Guid personId, CancellationToken cancellationToken = default)
    {
        CustomerLockRequests++;
        return Task.CompletedTask;
    }

    /// <summary> Lead code lock requests (SPEC 61). </summary>
    public int LeadCodeLockRequests { get; private set; }

    public Task AcquireLeadCodeLockAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        LeadCodeLockRequests++;
        return Task.CompletedTask;
    }

    public void Dispose() => inner.Dispose();
}
