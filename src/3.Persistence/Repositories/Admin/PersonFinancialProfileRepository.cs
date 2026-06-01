using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Domain.Admin;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;



namespace JOIN.Persistence.Repositories.Admin;



/// <summary>
/// EF Core repository for <see cref="PersonFinancialProfile"/> with queries that support current-profile orchestration.
/// </summary>
public sealed class PersonFinancialProfileRepository : GenericRepository<PersonFinancialProfile>, IPersonFinancialProfileRepository
{
    public PersonFinancialProfileRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersonFinancialProfile>> GetActiveCurrentAsync(
        Guid companyId,
        Guid personId,
        Guid? excludeProfileId = null,
        CancellationToken cancellationToken = default)
    {
        var query = ActiveForPersonQuery(companyId, personId)
            .Where(profile => profile.IsCurrent);

        if (excludeProfileId.HasValue)
        {
            query = query.Where(profile => profile.Id != excludeProfileId.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PersonFinancialProfile?> GetMostRecentActiveAsync(
        Guid companyId,
        Guid personId,
        Guid excludeProfileId,
        CancellationToken cancellationToken = default)
    {
        return await ActiveForPersonQuery(companyId, personId)
            .Where(profile => profile.Id != excludeProfileId)
            .OrderByDescending(profile => profile.DeclaredDate)
            .ThenByDescending(profile => profile.Created)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PersonFinancialProfile?> GetActiveByIdAsync(
        Guid id,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<PersonFinancialProfile>()
            .Where(profile => profile.CompanyId == companyId && profile.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private IQueryable<PersonFinancialProfile> ActiveForPersonQuery(Guid companyId, Guid? personId = null)
    {
        var query = _context.Set<PersonFinancialProfile>()
            .Where(profile => profile.CompanyId == companyId && profile.IsActive);

        if (personId.HasValue)
        {
            query = query.Where(profile => profile.PersonId == personId.Value);
        }

        return query;
    }
}
