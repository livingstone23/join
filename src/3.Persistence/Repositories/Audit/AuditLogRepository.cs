// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Audit;
using JOIN.Domain.Audit;
using JOIN.Persistence.Contexts;

namespace JOIN.Persistence.Repositories.Audit;

/// <summary>
/// Dapper-backed repository for the append-only <c>Security.AuditLogs</c> table.
/// Cross-DB: SQL Server vs PostgreSQL pagination branches inside <see cref="ListPagedAsync"/>.
/// </summary>
public sealed class AuditLogRepository(
    ApplicationDbContext dbContext,
    ISqlConnectionFactory connectionFactory)
    : IAuditLogRepository
{
    private readonly ApplicationDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task<int> InsertAsync(AuditLog entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        const string sql = """
            INSERT INTO [Security].[AuditLogs]
                (Id, EntityName, EntityId, EntityLabel, Action,
                 CompanyId, ChangedBy, ChangedAtUtc, IpAddress,
                 OldValuesJson, NewValuesJson, MetadataJson)
            VALUES
                (@Id, @EntityName, @EntityId, @EntityLabel, @Action,
                 @CompanyId, @ChangedBy, @ChangedAtUtc, @IpAddress,
                 @OldValuesJson, @NewValuesJson, @MetadataJson);
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(sql, entry, cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<int> InsertManyAsync(IEnumerable<AuditLog> entries, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        const string sql = """
            INSERT INTO [Security].[AuditLogs]
                (Id, EntityName, EntityId, EntityLabel, Action,
                 CompanyId, ChangedBy, ChangedAtUtc, IpAddress,
                 OldValuesJson, NewValuesJson, MetadataJson)
            VALUES
                (@Id, @EntityName, @EntityId, @EntityLabel, @Action,
                 @CompanyId, @ChangedBy, @ChangedAtUtc, @IpAddress,
                 @OldValuesJson, @NewValuesJson, @MetadataJson);
            """;

        var list = entries as IList<AuditLog> ?? entries.ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(sql, list, cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<AuditLog> Items, IReadOnlyDictionary<string, string> ChangedByNames, int TotalCount)> ListPagedAsync(
        Guid? companyId,
        string? entityName,
        Guid? entityId,
        string? changedBy,
        string? action,
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        var offset = (pageNumber - 1) * pageSize;

        // Cross-DB pagination: SQL Server uses OFFSET/FETCH NEXT, PostgreSQL uses LIMIT/OFFSET.
        var isPostgres = _dbContext.Database.ProviderName?.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true;
        var paginationClause = isPostgres
            ? "LIMIT @pageSize OFFSET @offset"
            : "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

        // Each optional filter uses the (@Param IS NULL OR Column = @Param) idiom so the
        // same SQL handles both "filter present" and "no filter" with one execution plan.
        var whereClause = """
            WHERE (@companyId IS NULL OR a.CompanyId = @companyId)
              AND (@entityName IS NULL OR a.EntityName = @entityName)
              AND (@entityId IS NULL OR a.EntityId = @entityId)
              AND (@changedBy IS NULL OR a.ChangedBy = @changedBy)
              AND (@action IS NULL OR a.Action = @action)
              AND (@fromUtc IS NULL OR a.ChangedAtUtc >= @fromUtc)
              AND (@toUtcExclusive IS NULL OR a.ChangedAtUtc < @toUtcExclusive)
            """;

        var pageSql = $"""
            SELECT
                a.Id,
                a.EntityName,
                a.EntityId,
                a.EntityLabel,
                a.Action,
                a.CompanyId,
                a.ChangedBy,
                a.ChangedAtUtc,
                a.IpAddress,
                a.OldValuesJson,
                a.NewValuesJson,
                a.MetadataJson
            FROM [Security].[AuditLogs] a
            {whereClause}
            ORDER BY a.ChangedAtUtc DESC, a.Id DESC
            {paginationClause};

            SELECT COUNT(*)
            FROM [Security].[AuditLogs] a
            {whereClause};
            """;

        // Second query: resolve ChangedByName per affected actor in the page (not the full table).
        // a.ChangedBy <> 'System' guards against the CAST raising on a non-Guid literal.
        var namesSql = $"""
            SELECT a.ChangedBy, u.FirstName + ' ' + u.LastName AS FullName
            FROM [Security].[AuditLogs] a
            LEFT JOIN [Security].[Users] u
                ON u.Id = TRY_CAST(a.ChangedBy AS uniqueidentifier)
            WHERE a.ChangedBy <> 'System'
              AND (@companyId IS NULL OR a.CompanyId = @companyId)
              AND (@entityName IS NULL OR a.EntityName = @entityName)
              AND (@entityId IS NULL OR a.EntityId = @entityId)
              AND (@changedBy IS NULL OR a.ChangedBy = @changedBy)
              AND (@action IS NULL OR a.Action = @action)
              AND (@fromUtc IS NULL OR a.ChangedAtUtc >= @fromUtc)
              AND (@toUtcExclusive IS NULL OR a.ChangedAtUtc < @toUtcExclusive)
              AND u.Id IS NOT NULL
            GROUP BY a.ChangedBy, u.FirstName, u.LastName;
            """;

        var parameters = new
        {
            companyId,
            entityName,
            entityId,
            changedBy,
            action,
            fromUtc,
            toUtcExclusive,
            offset,
            pageSize
        };

        using var connection = _connectionFactory.CreateConnection();

        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(pageSql, parameters, cancellationToken: ct));
        var items = (await multi.ReadAsync<AuditLogRow>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        var nameRows = await connection.QueryAsync<(string ChangedBy, string FullName)>(
            new CommandDefinition(namesSql, parameters, cancellationToken: ct));

        var names = nameRows
            .Where(r => !string.IsNullOrEmpty(r.FullName))
            .ToDictionary(r => r.ChangedBy, r => r.FullName);

        var mapped = items.Select(r => new AuditLog(r.Id)
        {
            EntityName = r.EntityName,
            EntityId = r.EntityId,
            EntityLabel = r.EntityLabel,
            Action = r.Action,
            CompanyId = r.CompanyId,
            ChangedBy = r.ChangedBy,
            ChangedAtUtc = r.ChangedAtUtc,
            IpAddress = r.IpAddress,
            OldValuesJson = r.OldValuesJson,
            NewValuesJson = r.NewValuesJson,
            MetadataJson = r.MetadataJson
        }).ToList();

        return (mapped, names, total);
    }

    /// <summary>
    /// Internal projection for the paged SELECT — keeps the domain entity untouched.
    /// </summary>
    private sealed class AuditLogRow
    {
        public Guid Id { get; set; }
        public string EntityName { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public string? EntityLabel { get; set; }
        public string Action { get; set; } = string.Empty;
        public Guid CompanyId { get; set; }
        public string ChangedBy { get; set; } = string.Empty;
        public DateTime ChangedAtUtc { get; set; }
        public string? IpAddress { get; set; }
        public string? OldValuesJson { get; set; }
        public string? NewValuesJson { get; set; }
        public string? MetadataJson { get; set; }
    }
}
