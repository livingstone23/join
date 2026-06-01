using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Domain.Admin;



namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles;



/// <summary>
/// Orchestrates the single-current-financial-profile invariant for a person within a tenant.
/// </summary>
public sealed class PersonFinancialProfileCurrentCoordinator(IPersonFinancialProfileRepository personFinancialProfileRepository)
{
    /// <summary>
    /// Archives all other active current financial profiles for the person.
    /// </summary>
    public async Task ArchiveOtherCurrentAsync(
        Guid companyId,
        Guid personId,
        Guid? excludeProfileId,
        CancellationToken cancellationToken)
    {
        var currentProfiles = await personFinancialProfileRepository.GetActiveCurrentAsync(
            companyId,
            personId,
            excludeProfileId,
            cancellationToken);

        foreach (var profile in currentProfiles)
        {
            profile.Archive();
            await personFinancialProfileRepository.UpdateAsync(profile);
        }
    }

    /// <summary>
    /// Promotes the most recent active financial profile as the new current (fallback after delete or deactivate).
    /// </summary>
    public async Task PromoteNextCurrentAsync(
        Guid companyId,
        Guid personId,
        Guid excludeProfileId,
        CancellationToken cancellationToken)
    {
        var nextCurrent = await personFinancialProfileRepository.GetMostRecentActiveAsync(
            companyId,
            personId,
            excludeProfileId,
            cancellationToken);

        if (nextCurrent is null)
        {
            return;
        }

        nextCurrent.SetAsCurrent();
        await personFinancialProfileRepository.UpdateAsync(nextCurrent);
    }
}
