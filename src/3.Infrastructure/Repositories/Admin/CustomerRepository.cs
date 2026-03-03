


using Dapper;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Domain.Admin;
using JOIN.Infrastructure.Contexts;



namespace JOIN.Infrastructure.Repositories.Admin;



/// <summary>
/// Hybrid repository implementation for the Customer entity.
/// Uses Entity Framework Core for state-changing commands to leverage Change Tracking and Auditing,
/// and Dapper for read-only queries to maximize performance and reduce memory overhead.
/// </summary>
public class CustomersRepository : ICustomersRepository
{

    private readonly ApplicationDbContext _dbContext;
    private readonly DapperContext _dapperContext;

    public CustomersRepository(ApplicationDbContext dbContext, DapperContext dapperContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _dapperContext = dapperContext ?? throw new ArgumentNullException(nameof(dapperContext));
    }

    #region Command Methods (Entity Framework Core)

    public async Task<bool> InsertAsync(Customer entity)
    {
        await _dbContext.Set<Customer>().AddAsync(entity);
        return true; 
        // Note: SaveChangesAsync is intentionally omitted. Commits are handled by IUnitOfWork.
    }

    public Task<bool> UpdateAsync(Customer entity)
    {
        _dbContext.Set<Customer>().Update(entity);
        return Task.FromResult(true);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var entity = await _dbContext.Set<Customer>().FindAsync(id);
        
        if (entity == null) 
            return false;

        _dbContext.Set<Customer>().Remove(entity);
        return true;
    }

    #endregion

    #region Query Methods (Dapper)

    public async Task<Customer?> GetAsync(Guid id)
    {
        using var connection = _dapperContext.CreateConnection();
        var query = "SELECT * FROM Admin.Customers WHERE Id = @Id AND GcRecord = 0";
        
        return await connection.QuerySingleOrDefaultAsync<Customer>(query, new { Id = id });
    }

    public async Task<IEnumerable<Customer>> GetAllAsync()
    {
        using var connection = _dapperContext.CreateConnection();
        var query = "SELECT * FROM Customers WHERE GcRecord = 0";
        
        return await connection.QueryAsync<Customer>(query);
    }

    public async Task<IEnumerable<Customer>> GetAllWithPaginationAsync(int pageNumber, int pageSize)
    {
        using var connection = _dapperContext.CreateConnection();
        
        var query = @"
            SELECT * FROM Customers 
            WHERE GcRecord = 0 
            ORDER BY Created DESC
            OFFSET @Offset ROWS 
            FETCH NEXT @PageSize ROWS ONLY";

        var parameters = new 
        { 
            Offset = (pageNumber - 1) * pageSize, 
            PageSize = pageSize 
        };

        return await connection.QueryAsync<Customer>(query, parameters);
    }

    public async Task<int> CountAsync()
    {
        using var connection = _dapperContext.CreateConnection();
        var query = "SELECT COUNT(*) FROM Customers WHERE GcRecord = 0";
        
        return await connection.ExecuteScalarAsync<int>(query);
    }

    #endregion
}
