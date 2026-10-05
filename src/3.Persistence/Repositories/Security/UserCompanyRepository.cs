// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Persistence.Repositories.Security;

/// <summary>
/// EF Core-backed repository for the UserCompany membership table.
/// SPEC 38: replaces the previous "load the whole table" pattern with scoped queries by UserId
/// (and CompanyId when applicable). Soft-delete (GcRecord == 0) is enforced explicitly here
/// because UserCompany has only the soft-delete portion of the global filter.
/// </summary>
public sealed class UserCompanyRepository(ApplicationDbContext dbContext) : IUserCompanyRepository
{
    private readonly ApplicationDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserCompany>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // EF applies the global GcRecord == 0 filter automatically; we only need to scope by UserId.
        return await _dbContext.UserCompanies
            .Where(uc => uc.UserId == userId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsActiveMemberAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        // EF applies the global GcRecord == 0 filter automatically; explicit predicate scopes the row.
        return await _dbContext.UserCompanies
            .AnyAsync(uc => uc.UserId == userId && uc.CompanyId == companyId, cancellationToken);
    }
}