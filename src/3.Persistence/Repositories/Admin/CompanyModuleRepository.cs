// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Domain.Admin;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Persistence.Repositories.Admin;

/// <summary>
/// EF Core-backed repository for tenant-scoped company module assignments.
/// Cross-tenant reads/writes (SuperAdmin via TenantResolver) bypass the global CompanyId filter
/// and apply an explicit CompanyId == tenantId predicate. SPEC 38.
/// </summary>
public sealed class CompanyModuleRepository(ApplicationDbContext dbContext) : ICompanyModuleRepository
{
    private readonly ApplicationDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <inheritdoc />
    public Task<CompanyModule?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
    {
        // SPEC 38: Skip the global CompanyId filter so SuperAdmin cross-tenant edits via
        // TenantResolver still resolve the row. Explicit tenant + soft-delete filter is defense-in-depth.
        return _dbContext.CompanyModules
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(cm => cm.Id == id && cm.CompanyId == tenantId && cm.GcRecord == 0, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsActiveAssignmentAsync(Guid companyId, Guid moduleId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Same justification as GetByIdForUpdateAsync: skip global filter, enforce tenant + soft-delete explicitly.
        return await _dbContext.CompanyModules
            .IgnoreQueryFilters()
            .AnyAsync(cm => cm.CompanyId == companyId && cm.ModuleId == moduleId && cm.GcRecord == 0, cancellationToken);
    }
}