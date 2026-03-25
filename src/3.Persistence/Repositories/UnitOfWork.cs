


using System.Collections;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin; // Add your specific interfaces
using JOIN.Domain.Admin;
using JOIN.Persistence.Contexts;
using JOIN.Persistence.Repositories.Admin;



namespace JOIN.Persistence.Repositories;



/// <summary>
/// Hybrid Unit of Work implementation.
/// Manages both generic and specialized repositories with Dapper support.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _dbContext;
    private readonly DapperContext _dapperContext;
    private Hashtable? _repositories;

    public UnitOfWork(ApplicationDbContext dbContext, DapperContext dapperContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _dapperContext = dapperContext ?? throw new ArgumentNullException(nameof(dapperContext));
    }

    // --- 1. SPECIFIC REPOSITORIES (EXPLICIT) ---
    // This explicit cast will now succeed because GetRepository returns the specific class type
    public ICustomersRepository Customers => (ICustomersRepository)GetRepository<Customer>();

    // --- 2. DYNAMIC REPOSITORY FACTORY ---
    public IGenericRepository<TEntity> GetRepository<TEntity>() where TEntity : class
    {
        if (_repositories == null) _repositories = new Hashtable();

        var type = typeof(TEntity).Name;

        if (!_repositories.Contains(type))
        {
            object repositoryInstance;

            // Check if the entity requires a specialized repository
            if (typeof(TEntity) == typeof(Customer))
            {
                // Instantiate the specific class that implements ICustomersRepository
                repositoryInstance = new CustomersRepository(_dbContext, _dapperContext);
            }
            else
            {
                // Default to standard Generic Repository for other modules
                var repositoryType = typeof(GenericRepository<>);
                repositoryInstance = Activator.CreateInstance(
                    repositoryType.MakeGenericType(typeof(TEntity)), _dbContext)!;
            }

            _repositories.Add(type, repositoryInstance);
        }

        return (IGenericRepository<TEntity>)_repositories[type]!;
    }

    public async Task<int> SaveAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
