using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Domain.Admin;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;



namespace JOIN.Persistence.Repositories.Admin;



/// <summary>
/// EF Core repository for <see cref="PersonAddress"/> with queries that support default-address orchestration.
/// </summary>
public sealed class PersonAddressRepository : GenericRepository<PersonAddress>, IPersonAddressRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PersonAddressRepository"/> class.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    public PersonAddressRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersonAddress>> GetActiveWithDefaultAsync(
        Guid companyId,
        Guid personId,
        Guid? excludeAddressId = null,
        CancellationToken cancellationToken = default)
    {
        var query = ActiveForPersonQuery(companyId, personId)
            .Where(address => address.IsDefault);

        if (excludeAddressId.HasValue)
        {
            query = query.Where(address => address.Id != excludeAddressId.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PersonAddress?> GetMostRecentActiveAsync(
        Guid companyId,
        Guid personId,
        Guid excludeAddressId,
        CancellationToken cancellationToken = default)
    {
        return await ActiveForPersonQuery(companyId, personId)
            .Where(address => address.Id != excludeAddressId)
            .OrderByDescending(address => address.Created)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PersonAddress?> GetActiveByIdAsync(
        Guid id,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await ActiveForPersonQuery(companyId)
            .FirstOrDefaultAsync(address => address.Id == id, cancellationToken);
    }

    private IQueryable<PersonAddress> ActiveForPersonQuery(Guid companyId, Guid? personId = null)
    {
        var query = _context.Set<PersonAddress>()
            .Where(address => address.CompanyId == companyId && address.IsActive);

        if (personId.HasValue)
        {
            query = query.Where(address => address.PersonId == personId.Value);
        }

        return query;
    }
}
