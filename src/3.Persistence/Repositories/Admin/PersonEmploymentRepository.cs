using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Domain.Admin;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;



namespace JOIN.Persistence.Repositories.Admin;



/// <summary>
/// EF Core repository for <see cref="PersonEmployment"/> with queries that support current-employment orchestration.
/// </summary>
public sealed class PersonEmploymentRepository : GenericRepository<PersonEmployment>, IPersonEmploymentRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PersonEmploymentRepository"/> class.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    public PersonEmploymentRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersonEmployment>> GetActiveCurrentAsync(
        Guid companyId,
        Guid personId,
        Guid? excludeEmploymentId = null,
        CancellationToken cancellationToken = default)
    {
        var query = ActiveForPersonQuery(companyId, personId)
            .Where(employment => employment.IsCurrent);

        if (excludeEmploymentId.HasValue)
        {
            query = query.Where(employment => employment.Id != excludeEmploymentId.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PersonEmployment?> GetMostRecentActiveAsync(
        Guid companyId,
        Guid personId,
        Guid excludeEmploymentId,
        CancellationToken cancellationToken = default)
    {
        return await ActiveForPersonQuery(companyId, personId)
            .Where(employment => employment.Id != excludeEmploymentId)
            .OrderByDescending(employment => employment.Created)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PersonEmployment?> GetActiveByIdAsync(
        Guid id,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await ActiveForPersonQuery(companyId)
            .FirstOrDefaultAsync(employment => employment.Id == id, cancellationToken);
    }

    private IQueryable<PersonEmployment> ActiveForPersonQuery(Guid companyId, Guid? personId = null)
    {
        var query = _context.Set<PersonEmployment>()
            .Where(employment => employment.CompanyId == companyId && employment.IsActive);

        if (personId.HasValue)
        {
            query = query.Where(employment => employment.PersonId == personId.Value);
        }

        return query;
    }
}
