


using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Infrastructure.Contexts;



namespace JOIN.Infrastructure.Repositories;



/// <summary>
/// Implementation of the Unit of Work pattern.
/// Coordinates the work of multiple repositories by sharing a single ApplicationDbContext,
/// guaranteeing that all operations succeed or fail as a single unit.
/// </summary>
public class UnitOfWork : IUnitOfWork
{

    private readonly ApplicationDbContext _dbContext;

    public ICustomersRepository Customers { get; }


    public UnitOfWork(
        ApplicationDbContext dbContext, 
        ICustomersRepository customersRepository)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        Customers = customersRepository ?? throw new ArgumentNullException(nameof(customersRepository));
    }

    public async Task<int> SaveAsync(CancellationToken cancellationToken = default)
    {
        // Triggers EF Core's SaveChangesAsync, automatically invoking the AuditableEntitySaveChangesInterceptor.
        return await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

}
