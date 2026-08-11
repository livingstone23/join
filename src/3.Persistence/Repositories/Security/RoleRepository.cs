using Dapper;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Persistence.Repositories.Security;

/// <summary>
/// Hybrid repository for ApplicationRole: Dapper for reads, EF Core for writes and tracked updates,
/// raw SQL for soft delete to avoid Identity's role manager caching.
/// </summary>
public sealed class RoleRepository(
    ApplicationDbContext dbContext,
    ISqlConnectionFactory connectionFactory)
    : IRoleRepository
{
    private readonly ApplicationDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task<RoleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                Id,
                Name,
                NormalizedName,
                Description,
                IsSystemDefault,
                CreatedBy,
                Created
            FROM [Security].[Roles]
            WHERE Id = @Id AND GcRecord = 0
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<RoleDto>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<RoleDto> Items, int Total)> GetPagedAsync(
        string? nameFilter,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Default to SQL Server pagination. The project runs on SQL Server today; when Postgres is wired,
        // branch on _dbContext.Database.ProviderName and switch to "LIMIT @pageSize OFFSET @offset".
        var offset = (page - 1) * pageSize;

        var whereClause = """
            WHERE (@nameFilter IS NULL OR Name LIKE '%' + @nameFilter + '%')
              AND (@isActive IS NULL
                   OR ((@isActive = 1 AND GcRecord = 0)
                       OR (@isActive = 0 AND GcRecord <> 0)))
            """;

        var countSql = $"SELECT COUNT(*) FROM [Security].[Roles] {whereClause};";
        var pageSql = $"""
            SELECT
                Id,
                Name,
                NormalizedName,
                Description,
                IsSystemDefault,
                CreatedBy,
                Created
            FROM [Security].[Roles]
            {whereClause}
            ORDER BY Name ASC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
            """;

        var parameters = new
        {
            nameFilter,
            isActive,
            offset,
            pageSize
        };

        using var connection = _connectionFactory.CreateConnection();
        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

        var items = await connection.QueryAsync<RoleDto>(
            new CommandDefinition(pageSql, parameters, cancellationToken: cancellationToken));

        return (items.AsList(), total);
    }

    /// <inheritdoc />
    public async Task AddAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        // The handler is expected to follow up with IUnitOfWork.SaveChangesAsync so multiple
        // aggregates (e.g. role + role audit shadow rows) commit atomically.
        await _dbContext.ApplicationRoles.AddAsync(role, cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        // The caller hands us a tracked entity loaded via GetByIdForUpdateAsync; we mark it Modified
        // so the UPDATE is unconditional and the handler can call IUnitOfWork.SaveChangesAsync.
        _dbContext.ApplicationRoles.Update(role);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByNameAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM [Security].[Roles]
                    WHERE NormalizedName = @NormalizedName AND GcRecord = 0
                ) THEN CAST(1 AS bit)
                ELSE CAST(0 AS bit)
            END;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { NormalizedName = normalizedName }, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByNameExceptIdAsync(string normalizedName, Guid excludedId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM [Security].[Roles]
                    WHERE NormalizedName = @NormalizedName
                      AND Id <> @ExcludedId
                      AND GcRecord = 0
                ) THEN CAST(1 AS bit)
                ELSE CAST(0 AS bit)
            END;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { NormalizedName = normalizedName, ExcludedId = excludedId }, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<ApplicationRole?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Tracked read so the caller can mutate and call SaveChanges via UoW or our UpdateAsync.
        return await _dbContext.ApplicationRoles
            .FirstOrDefaultAsync(r => r.Id == id && r.GcRecord == 0, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAndActiveAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM [Security].[Roles]
                    WHERE Id = @RoleId AND GcRecord = 0
                ) THEN CAST(1 AS bit)
                ELSE CAST(0 AS bit)
            END;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { RoleId = roleId }, cancellationToken: cancellationToken));
    }
}
