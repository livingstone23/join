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
}
