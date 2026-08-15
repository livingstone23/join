using System.Text;
using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Common;
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
                    FROM Security.RoleSystemOptions rso
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
            FROM Security.RoleSystemOptions rso
            INNER JOIN Security.Roles ar ON ar.Id = rso.RoleId AND ar.GcRecord = 0
            INNER JOIN Security.SystemOptions so ON so.Id = rso.SystemOptionId AND so.GcRecord = 0
            INNER JOIN Common.Companies c ON c.Id = rso.CompanyId AND c.GcRecord = 0
            {whereBuilder};
            """;

        return await connection.QuerySingleOrDefaultAsync<RoleSystemOptionReadModel>(
            sql,
            new { Id = id, CompanyId = companyId });
    }

    /// <inheritdoc />
    public async Task<RoleSystemOptionNames?> GetNamesByIdAndCompanyAsync(
        Guid id,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        // EF-only readback: same DbContext / same connection as the outer TransactionBehavior
        // transaction. Avoids the cross-connection X-lock that a Dapper SELECT would trigger
        // on the row that the just-flushed INSERT/UPDATE is still holding (default SQL Server
        // isolation level: read committed, no RCSI). Read-your-writes holds inside the same
        // connection, so the freshly persisted row is visible here.
        var query =
            from rso in _context.Set<RoleSystemOption>().IgnoreQueryFilters()
            join role in _context.Set<ApplicationRole>() on rso.RoleId equals role.Id
            join option in _context.Set<SystemOption>().IgnoreQueryFilters() on rso.SystemOptionId equals option.Id
            join company in _context.Set<Company>().IgnoreQueryFilters() on rso.CompanyId equals company.Id
            where rso.Id == id
                  && rso.CompanyId == companyId
                  && rso.GcRecord == 0
                  && role.GcRecord == 0
                  && option.GcRecord == 0
                  && company.GcRecord == 0
            select new RoleSystemOptionNames(role.Name!, option.Name, company.Name);

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleSystemOption>> GetActiveByRoleAndCompanyAsync(
        Guid roleId,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        // Dapper over a separate connection. Safe because we read the *origin* role's rows,
        // not the new role's rows that the outer transaction is staging. Returns the full
        // entity so the clone path can copy every flag (incl. the 5 new ones from SPEC 22:
        // CanDownload, CanExport, CanExecute, IsVisibleMenu, OrderMenu).
        const string sql = """
            SELECT
                Id,
                CompanyId,
                RoleId,
                SystemOptionId,
                CanRead,
                CanCreate,
                CanUpdate,
                CanDelete,
                CanDownload,
                CanExport,
                CanExecute,
                IsVisibleMenu,
                OrderMenu,
                Created,
                CreatedBy,
                LastModified,
                LastModifiedBy,
                GcRecord
            FROM [Security].[RoleSystemOptions]
            WHERE RoleId = @RoleId
              AND CompanyId = @CompanyId
              AND GcRecord = 0;
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<RoleSystemOption>(
            new CommandDefinition(sql, new { RoleId = roleId, CompanyId = companyId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
