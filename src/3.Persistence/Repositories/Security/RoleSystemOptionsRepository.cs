using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Persistence.Repositories.Security;

/// <summary>
/// Hybrid repository for RoleSystemOption, using EF for writes and Dapper for optimized reads.
/// </summary>
public sealed class RoleSystemOptionsRepository(
    ApplicationDbContext dbContext,
    ISqlConnectionFactory connectionFactory)
    : GenericRepository<RoleSystemOption>(dbContext), IRoleSystemOptionsRepository
{
    /// <inheritdoc />
    public async Task<RoleSystemOption?> GetTrackedActiveByIdAndCompanyAsync(
        Guid id,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<RoleSystemOption>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                r => r.Id == id && r.CompanyId == companyId && r.GcRecord == 0,
                cancellationToken);
    }

    public async Task<bool> ExistsByRoleAndOptionAsync(Guid companyId, Guid roleId, Guid systemOptionId)
    {
        using var connection = connectionFactory.CreateConnection();
        const string sql = """
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM Admin.RoleSystemOptions rso
                    WHERE rso.CompanyId = @CompanyId
                      AND rso.RoleId = @RoleId
                      AND rso.SystemOptionId = @SystemOptionId
                      AND rso.GcRecord = 0
                ) THEN CAST(1 AS bit)
                ELSE CAST(0 AS bit)
            END
            """;

        return await connection.ExecuteScalarAsync<bool>(sql, new
        {
            CompanyId = companyId,
            RoleId = roleId,
            SystemOptionId = systemOptionId
        });
    }

    public async Task<RoleSystemOptionReadModel?> GetWithNamesAsync(Guid id, Guid? companyId = null)
    {
        using var connection = connectionFactory.CreateConnection();

        var whereBuilder = new StringBuilder("WHERE rso.Id = @Id AND rso.GcRecord = 0");
        if (companyId.HasValue)
        {
            whereBuilder.Append(" AND rso.CompanyId = @CompanyId");
        }

        var sql = $"""
            SELECT
                rso.Id,
                rso.CompanyId,
                rso.RoleId,
                ar.Name AS RoleName,
                rso.SystemOptionId,
                so.Name AS SystemOptionName,
                c.Name AS CompanyName,
                rso.CanRead,
                rso.CanCreate,
                rso.CanUpdate,
                rso.CanDelete,
                rso.Created
            FROM Admin.RoleSystemOptions rso
            INNER JOIN Security.Roles ar ON ar.Id = rso.RoleId
            INNER JOIN Security.SystemOptions so ON so.Id = rso.SystemOptionId
            INNER JOIN Common.Companies c ON c.Id = rso.CompanyId AND c.GcRecord = 0
            {whereBuilder};
            """;

        return await connection.QuerySingleOrDefaultAsync<RoleSystemOptionReadModel>(
            sql,
            new { Id = id, CompanyId = companyId });
    }

    public async Task<(IReadOnlyList<RoleSystemOptionReadModel> Items, int TotalCount)> GetPagedWithNamesAsync(
        RoleSystemOptionQueryFilter filter,
        bool ignoreCompanyFilter = false,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        var whereBuilder = new StringBuilder(
            "WHERE rso.GcRecord = 0 AND ar.GcRecord = 0 AND so.GcRecord = 0 AND c.GcRecord = 0");

        if (!ignoreCompanyFilter || filter.CompanyId.HasValue)
        {
            whereBuilder.Append(" AND rso.CompanyId = @CompanyId");
            parameters.Add("CompanyId", filter.CompanyId);
        }

        if (filter.RoleId.HasValue)
        {
            whereBuilder.Append(" AND rso.RoleId = @RoleId");
            parameters.Add("RoleId", filter.RoleId.Value);
        }

        if (filter.SystemOptionId.HasValue)
        {
            whereBuilder.Append(" AND rso.SystemOptionId = @SystemOptionId");
            parameters.Add("SystemOptionId", filter.SystemOptionId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.RoleName))
        {
            whereBuilder.Append(" AND ar.Name LIKE @RoleNamePattern");
            parameters.Add("RoleNamePattern", $"%{filter.RoleName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(filter.SystemOptionName))
        {
            whereBuilder.Append(" AND so.Name LIKE @SystemOptionNamePattern");
            parameters.Add("SystemOptionNamePattern", $"%{filter.SystemOptionName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(filter.CompanyName))
        {
            whereBuilder.Append(" AND c.Name LIKE @CompanyNamePattern");
            parameters.Add("CompanyNamePattern", $"%{filter.CompanyName.Trim()}%");
        }

        if (filter.CanRead.HasValue)
        {
            whereBuilder.Append(" AND rso.CanRead = @CanRead");
            parameters.Add("CanRead", filter.CanRead.Value);
        }

        if (filter.CanCreate.HasValue)
        {
            whereBuilder.Append(" AND rso.CanCreate = @CanCreate");
            parameters.Add("CanCreate", filter.CanCreate.Value);
        }

        if (filter.CanUpdate.HasValue)
        {
            whereBuilder.Append(" AND rso.CanUpdate = @CanUpdate");
            parameters.Add("CanUpdate", filter.CanUpdate.Value);
        }

        if (filter.CanDelete.HasValue)
        {
            whereBuilder.Append(" AND rso.CanDelete = @CanDelete");
            parameters.Add("CanDelete", filter.CanDelete.Value);
        }

        parameters.Add("Offset", filter.Offset);
        parameters.Add("PageSize", filter.PageSize);
        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                rso.Id,
                rso.CompanyId,
                rso.RoleId,
                ar.Name AS RoleName,
                rso.SystemOptionId,
                so.Name AS SystemOptionName,
                c.Name AS CompanyName,
                rso.CanRead,
                rso.CanCreate,
                rso.CanUpdate,
                rso.CanDelete,
                rso.Created
            FROM Admin.RoleSystemOptions rso
            INNER JOIN Security.Roles ar ON ar.Id = rso.RoleId
            INNER JOIN Security.SystemOptions so ON so.Id = rso.SystemOptionId
            INNER JOIN Common.Companies c ON c.Id = rso.CompanyId AND c.GcRecord = 0
            {whereClause}
            ORDER BY c.Name ASC, ar.Name ASC, so.Name ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Admin.RoleSystemOptions rso
            INNER JOIN Security.Roles ar ON ar.Id = rso.RoleId
            INNER JOIN Security.SystemOptions so ON so.Id = rso.SystemOptionId
            INNER JOIN Common.Companies c ON c.Id = rso.CompanyId AND c.GcRecord = 0
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<RoleSystemOptionReadModel>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();
        return (items, totalCount);
    }

    private static string GetPaginationClause(IDbConnection connection)
        => connection.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? "LIMIT @PageSize OFFSET @Offset"
            : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
}
