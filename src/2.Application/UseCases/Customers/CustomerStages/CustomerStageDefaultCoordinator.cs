using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Customers;

namespace JOIN.Application.UseCases.Customers.CustomerStages;

/// <summary>
/// Keeps exactly one live default stage per company (SPEC 55 rule 8, <c>UX_CustomerStages_Company_Default</c>).
/// </summary>
public sealed class CustomerStageDefaultCoordinator(IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Unmarks every other live default stage of the company and saves right away, so the unique default index
    /// never sees two defaults when the caller marks the new one. Runs inside the command's transaction.
    /// </summary>
    public async Task ClearOtherDefaultsAsync(Guid companyId, Guid exceptStageId, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.GetRepository<CustomerStage>();
        var others = (await repository.GetAllAsync())
            .Where(s => s.CompanyId == companyId && s.GcRecord == 0 && s.IsDefault && s.Id != exceptStageId)
            .ToList();

        if (others.Count == 0)
        {
            return;
        }

        foreach (var stage in others)
        {
            stage.UnmarkAsDefault();
            await repository.UpdateAsync(stage);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
