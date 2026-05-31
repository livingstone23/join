using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Domain.Admin;



namespace JOIN.Application.UseCases.Admin.PersonAddresses;



/// <summary>
/// Orchestrates the single-default-address invariant for a person within a tenant.
/// </summary>
public sealed class PersonAddressDefaultCoordinator(IPersonAddressRepository personAddressRepository)
{
    /// <summary>
    /// Clears the default flag on all other active default addresses for the person.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="personId">The person identifier.</param>
    /// <param name="excludeAddressId">Optional address to exclude from clearing.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    public async Task ClearOtherDefaultsAsync(
        Guid companyId,
        Guid personId,
        Guid? excludeAddressId,
        CancellationToken cancellationToken)
    {
        var currentDefaults = await personAddressRepository.GetActiveWithDefaultAsync(
            companyId,
            personId,
            excludeAddressId,
            cancellationToken);

        foreach (var address in currentDefaults)
        {
            address.RemoveDefault();
            await personAddressRepository.UpdateAsync(address);
        }
    }

    /// <summary>
    /// Promotes the most recently created active address as the new default (fallback after delete).
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="personId">The person identifier.</param>
    /// <param name="excludeAddressId">The address being removed or deactivated.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    public async Task PromoteNextDefaultAsync(
        Guid companyId,
        Guid personId,
        Guid excludeAddressId,
        CancellationToken cancellationToken)
    {
        var nextDefault = await personAddressRepository.GetMostRecentActiveAsync(
            companyId,
            personId,
            excludeAddressId,
            cancellationToken);

        if (nextDefault is null)
        {
            return;
        }

        nextDefault.SetAsDefault();
        await personAddressRepository.UpdateAsync(nextDefault);
    }
}
