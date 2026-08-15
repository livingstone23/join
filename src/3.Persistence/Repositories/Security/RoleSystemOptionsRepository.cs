using System.Text;
using Dapper;
using JOIN.Application.DTO.Security;
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

    /// <inheritdoc />
    public async Task<RoleSystemOptionMatrixDto?> GetMatrixByRoleAsync(
        Guid roleId,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        // Two-step query on the same connection:
        //  1. Verify the role exists & is active (returns Name) — defense in depth; the handler
        //     also calls ExistsAndActiveAsync first. Returns null if not.
        //  2. Pull every active SystemOption with the granted flags from RoleSystemOptions
        //     (LEFT JOIN, collapses to all-false when no row exists). Groups by module in C#.
        using var connection = connectionFactory.CreateConnection();

        const string roleSql = """
            SELECT Name
            FROM [Security].[Roles]
            WHERE Id = @RoleId AND GcRecord = 0;
            """;
        var roleName = await connection.QuerySingleOrDefaultAsync<string?>(
            new CommandDefinition(roleSql, new { RoleId = roleId }, cancellationToken: cancellationToken));
        if (roleName is null)
        {
            return null;
        }

        const string matrixSql = """
            SELECT
                m.Id   AS ModuleId,
                m.Name AS ModuleName,
                m.[Order] AS ModuleOrder,
                o.Id   AS OptionId,
                o.Name AS OptionName,
                o.Route AS OptionRoute,
                o.OrderMenu AS OptionOrderMenu,
                o.CanRead     AS SupportCanRead,
                o.CanCreate   AS SupportCanCreate,
                o.CanUpdate   AS SupportCanUpdate,
                o.CanDelete   AS SupportCanDelete,
                o.CanDownload AS SupportCanDownload,
                o.CanExport   AS SupportCanExport,
                o.CanExecute  AS SupportCanExecute,
                ISNULL(rso.CanRead,     0) AS GrantedCanRead,
                ISNULL(rso.CanCreate,   0) AS GrantedCanCreate,
                ISNULL(rso.CanUpdate,   0) AS GrantedCanUpdate,
                ISNULL(rso.CanDelete,   0) AS GrantedCanDelete,
                ISNULL(rso.CanDownload, 0) AS GrantedCanDownload,
                ISNULL(rso.CanExport,   0) AS GrantedCanExport,
                ISNULL(rso.CanExecute,  0) AS GrantedCanExecute
            FROM [Admin].[SystemModules] m
            INNER JOIN [Security].[SystemOptions] o
                ON o.ModuleId = m.Id AND o.GcRecord = 0
            LEFT JOIN [Security].[RoleSystemOptions] rso
                ON rso.SystemOptionId = o.Id
               AND rso.RoleId = @RoleId
               AND rso.CompanyId = @CompanyId
               AND rso.GcRecord = 0
            WHERE m.GcRecord = 0 AND m.IsActive = 1
            ORDER BY
                ISNULL(m.[Order], 2147483647) ASC,
                m.Name ASC,
                ISNULL(o.OrderMenu, 2147483647) ASC,
                o.Name ASC;
            """;

        var rows = (await connection.QueryAsync<MatrixRow>(
            new CommandDefinition(matrixSql, new { RoleId = roleId, CompanyId = companyId }, cancellationToken: cancellationToken))).AsList();

        var modules = rows
            .GroupBy(r => new { r.ModuleId, r.ModuleName, r.ModuleOrder })
            .OrderBy(g => g.Key.ModuleOrder ?? int.MaxValue)
            .ThenBy(g => g.Key.ModuleName)
            .Select(g => new RoleSystemOptionMatrixModuleDto(
                ModuleId: g.Key.ModuleId,
                ModuleName: g.Key.ModuleName,
                Options: g
                    .OrderBy(r => r.OptionOrderMenu ?? int.MaxValue)
                    .ThenBy(r => r.OptionName)
                    .Select(r => new RoleSystemOptionMatrixOptionDto(
                        SystemOptionId: r.OptionId,
                        Name: r.OptionName,
                        Route: r.OptionRoute,
                        Supports: new RoleSystemOptionSupportFlags(
                            r.SupportCanRead, r.SupportCanCreate, r.SupportCanUpdate,
                            r.SupportCanDelete, r.SupportCanDownload, r.SupportCanExport, r.SupportCanExecute),
                        Granted: new RoleSystemOptionGrantedFlags(
                            r.GrantedCanRead, r.GrantedCanCreate, r.GrantedCanUpdate,
                            r.GrantedCanDelete, r.GrantedCanDownload, r.GrantedCanExport, r.GrantedCanExecute)))
                    .ToList()))
            .ToList();

        return new RoleSystemOptionMatrixDto(roleId, roleName, modules);
    }

    /// <summary>
    /// Internal row shape used by <see cref="GetMatrixByRoleAsync"/> to flatten the
    /// module→option→LEFT-JOIN-grants result into a single list before re-grouping in memory.
    /// </summary>
    private sealed class MatrixRow
    {
        public Guid ModuleId { get; set; }
        public string ModuleName { get; set; } = string.Empty;
        public int? ModuleOrder { get; set; }
        public Guid OptionId { get; set; }
        public string OptionName { get; set; } = string.Empty;
        public string OptionRoute { get; set; } = string.Empty;
        public int? OptionOrderMenu { get; set; }
        public bool SupportCanRead { get; set; }
        public bool SupportCanCreate { get; set; }
        public bool SupportCanUpdate { get; set; }
        public bool SupportCanDelete { get; set; }
        public bool SupportCanDownload { get; set; }
        public bool SupportCanExport { get; set; }
        public bool SupportCanExecute { get; set; }
        public bool GrantedCanRead { get; set; }
        public bool GrantedCanCreate { get; set; }
        public bool GrantedCanUpdate { get; set; }
        public bool GrantedCanDelete { get; set; }
        public bool GrantedCanDownload { get; set; }
        public bool GrantedCanExport { get; set; }
        public bool GrantedCanExecute { get; set; }
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Guid> Created, IReadOnlyList<Guid> Updated, IReadOnlyList<Guid> Removed)>
        BulkUpsertAsync(
            Guid roleId,
            Guid companyId,
            IReadOnlyList<RoleSystemOption> newItems,
            CancellationToken cancellationToken = default)
    {
        // Dapper with explicit SQL transaction — no EF change tracker.
        // 1. FOR UPDATE locks the existing rows for (roleId, companyId), serializing against
        //    a concurrent PUT /{id} that may try to insert the same (roleId, systemOptionId).
        // 2. INSERT each item absent from the existing set.
        // 3. UPDATE each item present in both sets.
        // 4. Soft-delete (GcRecord = stamp) each existing item absent from newItems.
        // 5. COMMIT. ROLLBACK on any failure.
        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            const string existingSql = """
                SELECT Id, SystemOptionId
                FROM [Security].[RoleSystemOptions] WITH (UPDLOCK, HOLDLOCK)
                WHERE RoleId = @RoleId AND CompanyId = @CompanyId AND GcRecord = 0;
                """;

            var existing = (await connection.QueryAsync<(Guid Id, Guid SystemOptionId)>(
                new CommandDefinition(existingSql, new { RoleId = roleId, CompanyId = companyId }, transaction: transaction, cancellationToken: cancellationToken))).ToList();
            var existingByOption = existing.ToDictionary(e => e.SystemOptionId, e => e.Id);

            var incomingByOption = newItems.ToDictionary(i => i.SystemOptionId);

            var created = new List<Guid>();
            var updated = new List<Guid>();
            var removed = new List<Guid>();

            const string insertSql = """
                INSERT INTO [Security].[RoleSystemOptions]
                    (Id, CompanyId, RoleId, SystemOptionId,
                     CanRead, CanCreate, CanUpdate, CanDelete,
                     CanDownload, CanExport, CanExecute,
                     IsVisibleMenu, OrderMenu,
                     Created, CreatedBy, LastModified, LastModifiedBy, GcRecord)
                VALUES
                    (@Id, @CompanyId, @RoleId, @SystemOptionId,
                     @CanRead, @CanCreate, @CanUpdate, @CanDelete,
                     @CanDownload, @CanExport, @CanExecute,
                     @IsVisibleMenu, @OrderMenu,
                     @Created, @CreatedBy, NULL, NULL, 0);
                """;

            const string updateSql = """
                UPDATE [Security].[RoleSystemOptions]
                SET CanRead = @CanRead,
                    CanCreate = @CanCreate,
                    CanUpdate = @CanUpdate,
                    CanDelete = @CanDelete,
                    CanDownload = @CanDownload,
                    CanExport = @CanExport,
                    CanExecute = @CanExecute,
                    IsVisibleMenu = @IsVisibleMenu,
                    OrderMenu = @OrderMenu,
                    LastModified = SYSUTCDATETIME(),
                    LastModifiedBy = @LastModifiedBy
                WHERE Id = @Id;
                """;

            const string softDeleteSql = """
                UPDATE [Security].[RoleSystemOptions]
                SET GcRecord = 1,
                    LastModified = SYSUTCDATETIME(),
                    LastModifiedBy = @LastModifiedBy
                WHERE Id = @Id;
                """;

            foreach (var item in newItems)
            {
                if (existingByOption.TryGetValue(item.SystemOptionId, out var existingId))
                {
                    await connection.ExecuteAsync(new CommandDefinition(updateSql, new
                    {
                        Id = existingId,
                        item.CanRead, item.CanCreate, item.CanUpdate, item.CanDelete,
                        item.CanDownload, item.CanExport, item.CanExecute,
                        item.IsVisibleMenu, item.OrderMenu,
                        item.LastModifiedBy
                    }, transaction: transaction, cancellationToken: cancellationToken));
                    updated.Add(existingId);
                }
                else
                {
                    await connection.ExecuteAsync(new CommandDefinition(insertSql, new
                    {
                        item.Id, item.CompanyId, item.RoleId, item.SystemOptionId,
                        item.CanRead, item.CanCreate, item.CanUpdate, item.CanDelete,
                        item.CanDownload, item.CanExport, item.CanExecute,
                        item.IsVisibleMenu, item.OrderMenu,
                        item.Created, item.CreatedBy
                    }, transaction: transaction, cancellationToken: cancellationToken));
                    created.Add(item.Id);
                }
            }

            // Items that exist in the DB but not in the incoming set → soft-delete.
            var lastModifiedBy = newItems.FirstOrDefault()?.LastModifiedBy ?? newItems.FirstOrDefault()?.CreatedBy;
            foreach (var e in existing)
            {
                if (!incomingByOption.ContainsKey(e.SystemOptionId))
                {
                    await connection.ExecuteAsync(new CommandDefinition(softDeleteSql, new
                    {
                        Id = e.Id,
                        LastModifiedBy = lastModifiedBy
                    }, transaction: transaction, cancellationToken: cancellationToken));
                    removed.Add(e.Id);
                }
            }

            transaction.Commit();
            return (created, updated, removed);
        }
        catch
        {
            try { transaction.Rollback(); } catch { /* connection may already be torn down */ }
            throw;
        }
    }
}
