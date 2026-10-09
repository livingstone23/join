using System.Linq.Expressions;




namespace JOIN.Application.Interface.Persistence;



/// <summary>
/// Defines the foundational data access contract for all domain entities.
/// Strictly separates state-mutating operations (Commands) from read-only operations (Queries).
/// </summary>
/// <typeparam name="T">The domain entity type.</typeparam>
public interface IGenericRepository<T> where T : class
{
    #region Commands (State Mutation)
    
    Task<bool> InsertAsync(T entity);
    Task<bool> UpdateAsync(T entity);

    // No physical delete on purpose: the only delete in the system is the logical one
    // (BaseAuditableEntity.MarkAsDeleted / GcRecord). DeleteAsync was removed in SPEC 41.
    
    #endregion

    #region Queries (Read-Only & High Performance)
    
    Task<T?> GetAsync(Guid id);

    /// <summary>
    /// Fetches an entity by id bypassing any global query filter (e.g. the standard
    /// <c>GcRecord == 0</c> soft-delete filter), so a previously soft-deleted row can be
    /// located in order to reactivate it. <see cref="GetAsync"/> cannot be reused for this:
    /// EF Core's <c>FindAsync</c> applies global query filters, so it returns <c>null</c>
    /// for a soft-deleted id even though the row exists — silently turning a
    /// reactivate-if-soft-deleted branch into a no-op.
    /// </summary>
    Task<T?> GetIncludingDeletedAsync(Guid id);

    Task<IEnumerable<T>> GetAllAsync();

    /// <summary>
    /// Returns the rows matching <paramref name="predicate"/> bypassing every global query
    /// filter (soft-delete <c>GcRecord == 0</c> and tenant). The predicate is translated to SQL,
    /// so callers must scope it themselves (typically by <c>CompanyId</c>). Results are not
    /// tracked. Used where soft-deleted rows still matter, e.g. computing the next ticket code
    /// so a deleted ticket's code is never reissued.
    /// </summary>
    Task<IEnumerable<T>> GetAllIncludingDeletedAsync(Expression<Func<T, bool>> predicate);

    Task<IEnumerable<T>> GetAllWithPaginationAsync(int pageNumber, int pageSize);
    Task<int> CountAsync();
    
    #endregion
}