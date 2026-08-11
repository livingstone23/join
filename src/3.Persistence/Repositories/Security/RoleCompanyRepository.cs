// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Dapper;
using JOIN.Application.DTO.Security.RoleCompany;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Persistence.Repositories.Security;

/// <summary>
/// Hybrid repository for the RoleCompany junction.
/// Reads are Dapper-backed (raw SQL with JOIN to Security.Roles to project RoleName/IsSystemDefault);
/// writes are EF Core-backed (tracked entities + IUnitOfWork.SaveChangesAsync).
/// Soft delete follows the project convention: the command handler calls
/// <see cref="BaseAuditableEntity.MarkAsDeleted"/> on the loaded entity and
/// persists via <see cref="UpdateAsync"/> + IUnitOfWork.SaveChangesAsync.
/// No SoftDeleteAsync method exists.
/// </summary>
public sealed class RoleCompanyRepository(
    ApplicationDbContext dbContext,
    ISqlConnectionFactory connectionFactory)
    : IRoleCompanyRepository
{
    private readonly ApplicationDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task<RoleCompanyDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                rc.Id,
                rc.RoleId,
                r.Name AS RoleName,
                r.IsSystemDefault,
                rc.CreatedBy,
                rc.Created
            FROM [Security].[RoleCompanies] rc
            INNER JOIN [Security].[Roles] r
                ON rc.RoleId = r.Id AND r.GcRecord = 0
            WHERE rc.Id = @Id
              AND rc.CompanyId = @TenantId
              AND rc.GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<RoleCompanyDto>(
            new CommandDefinition(sql, new { Id = id, TenantId = tenantId }, cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<RoleCompanyListItemDto> Items, int Total)> GetPagedAsync(
        Guid tenantId,
        Guid? roleIdFilter,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var offset = (page - 1) * pageSize;

        // Cross-DB pagination: SQL Server uses OFFSET/FETCH NEXT, Postgres uses LIMIT/OFFSET.
        var paginationClause = _dbContext.Database.ProviderName?.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true
            ? "LIMIT @pageSize OFFSET @offset"
            : "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

        var whereClause = """
            WHERE rc.CompanyId = @TenantId
              AND rc.GcRecord = 0
              AND (@roleIdFilter IS NULL OR rc.RoleId = @roleIdFilter)
              AND (@isActive IS NULL
                   OR ((@isActive = 1 AND rc.GcRecord = 0)
                       OR (@isActive = 0 AND rc.GcRecord <> 0)))
            """;

        var countSql = $"SELECT COUNT(*) FROM [Security].[RoleCompanies] rc {whereClause};";

        var pageSql = $"""
            SELECT
                rc.Id,
                rc.RoleId,
                r.Name AS RoleName,
                r.IsSystemDefault,
                rc.Created
            FROM [Security].[RoleCompanies] rc
            INNER JOIN [Security].[Roles] r
                ON rc.RoleId = r.Id AND r.GcRecord = 0
            {whereClause}
            ORDER BY rc.Created DESC, rc.Id
            {paginationClause};
            """;

        var parameters = new
        {
            TenantId = tenantId,
            roleIdFilter,
            isActive,
            offset,
            pageSize
        };

        using var connection = _connectionFactory.CreateConnection();
        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

        var items = await connection.QueryAsync<RoleCompanyListItemDto>(
            new CommandDefinition(pageSql, parameters, cancellationToken: cancellationToken));

        return (items.AsList(), total);
    }

    /// <inheritdoc />
    public async Task AddAsync(RoleCompany entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _dbContext.RoleCompanies.AddAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateAsync(RoleCompany entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        // Caller hands us a tracked entity loaded via GetByIdForUpdateAsync; we mark it Modified
        // so the UPDATE is unconditional and the handler can call IUnitOfWork.SaveChangesAsync.
        _dbContext.RoleCompanies.Update(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<RoleCompany?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Tracked read with tenant + soft-delete filter. Skips the global query filter for tenant
        // (only soft-delete is global here) and enforces CompanyId == tenantId explicitly.
        // Defense-in-depth against cross-tenant manipulation: even if a caller passes an Id from
        // another tenant, this filter returns null.
        return await _dbContext.RoleCompanies
            .FirstOrDefaultAsync(rc => rc.Id == id && rc.CompanyId == tenantId && rc.GcRecord == 0, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsActiveLinkAsync(Guid roleId, Guid companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM [Security].[RoleCompanies]
                    WHERE RoleId = @RoleId
                      AND CompanyId = @CompanyId
                      AND GcRecord = 0
                ) THEN CAST(1 AS bit)
                ELSE CAST(0 AS bit)
            END;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { RoleId = roleId, CompanyId = companyId }, cancellationToken: cancellationToken));
    }
}
