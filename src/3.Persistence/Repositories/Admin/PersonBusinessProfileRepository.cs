using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Domain.Admin;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;



namespace JOIN.Persistence.Repositories.Admin;



/// <summary>
/// EF Core repository for <see cref="PersonBusinessProfile"/> with queries that support active-profile orchestration.
/// </summary>
public sealed class PersonBusinessProfileRepository : GenericRepository<PersonBusinessProfile>, IPersonBusinessProfileRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PersonBusinessProfileRepository"/> class.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    public PersonBusinessProfileRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersonBusinessProfile>> GetActiveProfilesAsync(
        Guid companyId,
        Guid personId,
        Guid? excludeProfileId = null,
        CancellationToken cancellationToken = default)
    {
        var query = ActiveForPersonQuery(companyId, personId);

        if (excludeProfileId.HasValue)
        {
            query = query.Where(profile => profile.Id != excludeProfileId.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PersonBusinessProfile?> GetActiveByIdAsync(
        Guid id,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<PersonBusinessProfile>()
            .Where(profile => profile.CompanyId == companyId && profile.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private IQueryable<PersonBusinessProfile> ActiveForPersonQuery(Guid companyId, Guid? personId = null)
    {
        var query = _context.Set<PersonBusinessProfile>()
            .Where(profile => profile.CompanyId == companyId && profile.IsActive);

        if (personId.HasValue)
        {
            query = query.Where(profile => profile.PersonId == personId.Value);
        }

        return query;
    }
}
