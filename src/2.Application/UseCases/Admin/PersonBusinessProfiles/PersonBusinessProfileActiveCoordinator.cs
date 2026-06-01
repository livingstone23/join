using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Domain.Admin;



namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles;



/// <summary>
/// Orchestrates the single-active-business-profile invariant for a person within a tenant.
/// </summary>
public sealed class PersonBusinessProfileActiveCoordinator(IPersonBusinessProfileRepository personBusinessProfileRepository)
{
    /// <summary>
    /// Deactivates all other currently active business profiles for the person.
    /// </summary>
    public async Task DeactivateOtherActiveProfilesAsync(
        Guid companyId,
        Guid personId,
        Guid? excludeProfileId,
        CancellationToken cancellationToken)
    {
        var activeProfiles = await personBusinessProfileRepository.GetActiveProfilesAsync(
            companyId,
            personId,
            excludeProfileId,
            cancellationToken);

        foreach (var profile in activeProfiles)
        {
            profile.Deactivate();
            await personBusinessProfileRepository.UpdateAsync(profile);
        }
    }
}
