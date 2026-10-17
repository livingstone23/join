using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Finance;

namespace JOIN.Application.UseCases.Finance.CompanyCurrencies;

/// <summary>
/// SPEC 56 rule 1: exactly one live base currency per company. Marking a currency as base unmarks the previous one
/// in the same transaction (the filtered unique index <c>UX_CompanyCurrencies_Company_Base</c> backs it up).
/// </summary>
public sealed class CompanyBaseCurrencyCoordinator(IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Unmarks every live base currency of <paramref name="companyId"/> other than <paramref name="keepId"/> and saves,
    /// so the next insert or update that sets <c>IsBase</c> does not violate the unique index.
    /// </summary>
    public async Task ClearOtherBasesAsync(Guid companyId, Guid keepId, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.GetRepository<CompanyCurrency>();
        var others = (await repository.GetAllIncludingDeletedAsync(c =>
                c.CompanyId == companyId && c.GcRecord == 0 && c.IsBase && c.Id != keepId))
            .ToList();

        if (others.Count == 0)
        {
            return;
        }

        foreach (var other in others)
        {
            other.IsBase = false;
            await repository.UpdateAsync(other);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The live base currency of the company, or <c>null</c>.</summary>
    public async Task<CompanyCurrency?> GetBaseAsync(Guid companyId) =>
        (await unitOfWork.GetRepository<CompanyCurrency>()
            .GetAllIncludingDeletedAsync(c => c.CompanyId == companyId && c.GcRecord == 0 && c.IsBase))
        .FirstOrDefault();
}
