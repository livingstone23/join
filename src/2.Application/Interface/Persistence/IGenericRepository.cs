


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
    Task<bool> DeleteAsync(Guid id);
    
    #endregion

    #region Queries (Read-Only & High Performance)
    
    Task<T?> GetAsync(Guid id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<IEnumerable<T>> GetAllWithPaginationAsync(int pageNumber, int pageSize);
    Task<int> CountAsync();
    
    #endregion
}