using JOIN.Application.Interface.Persistence;
using Microsoft.EntityFrameworkCore;
using JOIN.Persistence.Contexts;

namespace JOIN.Persistence.Repositories;


/// <summary>
/// Generic repository implementation providing basic CRUD operations for any entity type.
/// Strictly separates state-mutating operations (Commands) from read-only operations (Queries).
/// </summary>
/// <typeparam name="T"></typeparam> <summary>
/// 
/// </summary>
/// <typeparam name="T"></typeparam>
public class GenericRepository<T> : IGenericRepository<T> where T : class
{
    protected readonly ApplicationDbContext _context;

    public GenericRepository(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    // --- Commands ---
    public async Task<bool> InsertAsync(T entity)
    {
        await _context.Set<T>().AddAsync(entity);
        return true; 
    }

    public Task<bool> UpdateAsync(T entity)
    {
        _context.Set<T>().Update(entity);
        return Task.FromResult(true);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var entity = await _context.Set<T>().FindAsync(id);
        if (entity == null) return false;
        _context.Set<T>().Remove(entity);
        return true;
    }

    // --- Queries ---
    public virtual async Task<T?> GetAsync(Guid id) => await _context.Set<T>().FindAsync(id);

    public virtual async Task<IEnumerable<T>> GetAllAsync() => await _context.Set<T>().ToListAsync();

    public virtual async Task<IEnumerable<T>> GetAllWithPaginationAsync(int pageNumber, int pageSize)
    {
        return await _context.Set<T>()
            .AsNoTracking()
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public virtual async Task<int> CountAsync() => await _context.Set<T>().CountAsync();
}