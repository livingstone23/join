using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Domain.Admin;



namespace JOIN.Application.UseCases.Admin.PersonEmployments;



/// <summary>
/// Orchestrates the single-current-employment invariant for a person within a tenant.
/// </summary>
public sealed class PersonEmploymentCurrentCoordinator(IPersonEmploymentRepository personEmploymentRepository)
{
    /// <summary>
    /// Clears the current flag on all other active current employments for the person.
    /// </summary>
    public async Task ClearOtherCurrentAsync(
        Guid companyId,
        Guid personId,
        Guid? excludeEmploymentId,
        CancellationToken cancellationToken)
    {
        var currentEmployments = await personEmploymentRepository.GetActiveCurrentAsync(
            companyId,
            personId,
            excludeEmploymentId,
            cancellationToken);

        foreach (var employment in currentEmployments)
        {
            employment.RemoveCurrent();
            await personEmploymentRepository.UpdateAsync(employment);
        }
    }

    /// <summary>
    /// Promotes the most recently created active employment as the new current (fallback after delete or deactivate).
    /// </summary>
    public async Task PromoteNextCurrentAsync(
        Guid companyId,
        Guid personId,
        Guid excludeEmploymentId,
        CancellationToken cancellationToken)
    {
        var nextCurrent = await personEmploymentRepository.GetMostRecentActiveAsync(
            companyId,
            personId,
            excludeEmploymentId,
            cancellationToken);

        if (nextCurrent is null)
        {
            return;
        }

        nextCurrent.SetAsCurrent();
        await personEmploymentRepository.UpdateAsync(nextCurrent);
    }
}
