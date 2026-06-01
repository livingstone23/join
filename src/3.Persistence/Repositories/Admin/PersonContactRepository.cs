using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;



namespace JOIN.Persistence.Repositories.Admin;



/// <summary>
/// EF Core repository for <see cref="PersonContact"/> with queries that support primary-contact orchestration.
/// </summary>
public sealed class PersonContactRepository : GenericRepository<PersonContact>, IPersonContactRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PersonContactRepository"/> class.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    public PersonContactRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersonContact>> GetActiveWithPrimaryByTypeAsync(
        Guid companyId,
        Guid personId,
        ContactType contactType,
        Guid? excludeContactId = null,
        CancellationToken cancellationToken = default)
    {
        var query = ActiveForPersonQuery(companyId, personId, contactType)
            .Where(contact => contact.IsPrimary);

        if (excludeContactId.HasValue)
        {
            query = query.Where(contact => contact.Id != excludeContactId.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PersonContact?> GetMostRecentActiveByTypeAsync(
        Guid companyId,
        Guid personId,
        ContactType contactType,
        Guid excludeContactId,
        CancellationToken cancellationToken = default)
    {
        return await ActiveForPersonQuery(companyId, personId, contactType)
            .Where(contact => contact.Id != excludeContactId)
            .OrderByDescending(contact => contact.Created)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PersonContact?> GetActiveByIdAsync(
        Guid id,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await ActiveForPersonQuery(companyId)
            .FirstOrDefaultAsync(contact => contact.Id == id, cancellationToken);
    }

    private IQueryable<PersonContact> ActiveForPersonQuery(
        Guid companyId,
        Guid? personId = null,
        ContactType? contactType = null)
    {
        var query = _context.Set<PersonContact>()
            .Where(contact => contact.CompanyId == companyId && contact.IsActive);

        if (personId.HasValue)
        {
            query = query.Where(contact => contact.PersonId == personId.Value);
        }

        if (contactType.HasValue)
        {
            query = query.Where(contact => contact.ContactType == contactType.Value);
        }

        return query;
    }
}
