

using Dapper;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Domain.Admin;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;



namespace JOIN.Persistence.Repositories.Admin;


 
/// <summary>
/// Hybrid repository implementation for the Customer entity.
/// Inherits from GenericRepository to reuse EF Core command logic (Insert, Update, Delete).
/// Implements ICustomersRepository using Dapper for high-performance read-only queries.
/// </summary>
public class CustomersRepository : GenericRepository<Customer>, ICustomersRepository
{
    private readonly DapperContext _dapperContext;

    /// <summary>
    /// Initializes a new instance of the CustomersRepository.
    /// Passes the ApplicationDbContext to the base GenericRepository.
    /// </summary>
    /// <param name="dbContext">The EF Core database context.</param>
    /// <param name="dapperContext">The Dapper connection factory.</param>
    public CustomersRepository(ApplicationDbContext dbContext, DapperContext dapperContext) 
        : base(dbContext)
    {
        _dapperContext = dapperContext ?? throw new ArgumentNullException(nameof(dapperContext));
    }

    // NOTE: InsertAsync, UpdateAsync, and DeleteAsync are inherited from GenericRepository.
    // They use EF Core's Change Tracking and the AuditableInterceptor automatically.

    #region Optimized Query Methods (Dapper)

    /// <summary>
    /// Retrieves a single customer by ID using Dapper for performance.
    /// </summary>
    public override async Task<Customer?> GetAsync(Guid id)
    {
        using var connection = _dapperContext.CreateConnection();
        // Uses the 'Admin' schema as defined in the domain mapping.
        const string query = "SELECT * FROM Admin.Customers WHERE Id = @Id AND GcRecord = 0";
        
        return await connection.QuerySingleOrDefaultAsync<Customer>(query, new { Id = id });
    }

    /// <summary>
    /// Retrieves a single customer by ID including IdentificationType using EF Core.
    /// </summary>
    public async Task<Customer?> GetByIdWithIdentificationTypeAsync(Guid id)
    {
        return await _context.Set<Customer>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(c => c.IdentificationType)
            .Include(c => c.Addresses)
                .ThenInclude(a => a.StreetType)
            .Include(c => c.Addresses)
                .ThenInclude(a => a.Country)
            .Include(c => c.Addresses)
                .ThenInclude(a => a.Region)
            .Include(c => c.Addresses)
                .ThenInclude(a => a.Province)
            .Include(c => c.Addresses)
                .ThenInclude(a => a.Municipality)
            .FirstOrDefaultAsync(c => c.Id == id && c.GcRecord == 0);
    }

    /// <summary>
    /// Retrieves all active customers using Dapper to reduce memory overhead.
    /// </summary>
    public override async Task<IEnumerable<Customer>> GetAllAsync()
    {
        using var connection = _dapperContext.CreateConnection();
        const string query = "SELECT * FROM Admin.Customers WHERE GcRecord = 0";
        
        return await connection.QueryAsync<Customer>(query);
    }

    /// <summary>
    /// Retrieves a paginated list of customers using Dapper.
    /// </summary>
    public override async Task<IEnumerable<Customer>> GetAllWithPaginationAsync(int pageNumber, int pageSize)
    {
        using var connection = _dapperContext.CreateConnection();
        
        const string query = @"
            SELECT * FROM Admin.Customers 
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

    /// <summary>
    /// Counts the total number of active customers.
    /// </summary>
    public override async Task<int> CountAsync()
    {
        using var connection = _dapperContext.CreateConnection();
        const string query = "SELECT COUNT(*) FROM Admin.Customers WHERE GcRecord = 0";
        
        return await connection.ExecuteScalarAsync<int>(query);
    }

    #endregion
}