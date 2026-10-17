using JOIN.Application.Interface.Customers;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Customers.Teams;

namespace JOIN.Application.UseCases.Customers.SalesTeams;

/// <inheritdoc />
/// <remarks>Closes with today's date (or <c>ValidFrom</c> when the membership starts later) and clears the primary flag.</remarks>
public sealed class SalesTeamMembershipCloser(IUnitOfWork unitOfWork) : ISalesTeamMembershipCloser
{
    /// <inheritdoc />
    public async Task<int> CloseAllForUserAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repository = unitOfWork.GetRepository<SalesTeamMember>();
        var open = (await repository.GetAllIncludingDeletedAsync(m =>
                m.CompanyId == companyId && m.UserId == userId && m.GcRecord == 0 && m.ValidTo == null))
            .ToList();

        foreach (var row in open)
        {
            // The caller may act on another company than its token's, so the row is updated as read (no tenant filter).
            row.ValidTo = row.ValidFrom > today ? row.ValidFrom : today;
            row.IsPrimary = false;
            await repository.UpdateAsync(row);
        }

        return open.Count;
    }
}
