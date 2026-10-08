using System.Linq.Expressions;
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

    // Detached entities (e.g. materialized with AsNoTracking) keep the classic Update()
    // semantics. Tracked entities (the usual GetAsync/FindAsync → mutate → UpdateAsync flow)
    // must NOT go through Update(): BaseEntity assigns Id in its constructor, so Update()'s
    // graph walk sees a child appended to a collection (e.g. Ticket.AddLog → TicketLog) as an
    // existing row with a key and marks it Modified, producing an UPDATE that affects 0 rows
    // and a DbUpdateConcurrencyException. Untracked children found in a tracked root's
    // collections are new by construction (anything loaded from the DB would already be
    // tracked), so they are marked Added explicitly instead of relying on DetectChanges'
    // key-based inference. The root itself is still forced to Modified, exactly as Update()
    // did, so callers that check "SaveChangesAsync() > 0" keep succeeding on a no-op edit.
    public Task<bool> UpdateAsync(T entity)
    {
        var changeTracker = _context.ChangeTracker;
        var autoDetectChanges = changeTracker.AutoDetectChangesEnabled;
        changeTracker.AutoDetectChangesEnabled = false;

        try
        {
            var entry = _context.Entry(entity);
            if (entry.State == EntityState.Detached)
            {
                _context.Set<T>().Update(entity);
                return Task.FromResult(true);
            }

            if (entry.State == EntityState.Unchanged)
            {
                entry.State = EntityState.Modified;
            }

            foreach (var collection in entry.Collections)
            {
                if (collection.CurrentValue is null)
                {
                    continue;
                }

                foreach (var child in collection.CurrentValue)
                {
                    var childEntry = _context.Entry(child);
                    if (childEntry.State == EntityState.Detached)
                    {
                        childEntry.State = EntityState.Added;
                    }
                }
            }
        }
        finally
        {
            changeTracker.AutoDetectChangesEnabled = autoDetectChanges;
        }

        return Task.FromResult(true);
    }

    // --- Queries ---
    public virtual async Task<T?> GetAsync(Guid id) => await _context.Set<T>().FindAsync(id);

    // FindAsync (used by GetAsync above) applies global query filters, so it returns null
    // for a soft-deleted row even though it exists — silently no-oping any
    // reactivate-if-soft-deleted branch (e.g. ReplaceUserRolesCommandHandler re-adding a
    // previously removed role). IgnoreQueryFilters() + a property-based lookup on "Id"
    // bypasses that filter; every entity in this codebase derives from BaseEntity, which
    // declares Id as Guid.
    public virtual async Task<T?> GetIncludingDeletedAsync(Guid id) =>
        await _context.Set<T>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == id);

    public virtual async Task<IEnumerable<T>> GetAllAsync() => await _context.Set<T>().ToListAsync();

    public virtual async Task<IEnumerable<T>> GetAllIncludingDeletedAsync(Expression<Func<T, bool>> predicate) =>
        await _context.Set<T>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(predicate)
            .ToListAsync();

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