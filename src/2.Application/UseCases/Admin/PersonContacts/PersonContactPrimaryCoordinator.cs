using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;



namespace JOIN.Application.UseCases.Admin.PersonContacts;



/// <summary>
/// Orchestrates the single-primary-contact invariant per <see cref="ContactType"/> for a person within a tenant.
/// </summary>
public sealed class PersonContactPrimaryCoordinator(IPersonContactRepository personContactRepository)
{
    /// <summary>
    /// Clears the primary flag on all other active primary contacts of the same type for the person.
    /// </summary>
    public async Task ClearOtherPrimariesAsync(
        Guid companyId,
        Guid personId,
        ContactType contactType,
        Guid? excludeContactId,
        CancellationToken cancellationToken)
    {
        var currentPrimaries = await personContactRepository.GetActiveWithPrimaryByTypeAsync(
            companyId,
            personId,
            contactType,
            excludeContactId,
            cancellationToken);

        foreach (var contact in currentPrimaries)
        {
            contact.RemovePrimary();
            await personContactRepository.UpdateAsync(contact);
        }
    }

    /// <summary>
    /// Promotes the most recently created active contact of the given type as the new primary (fallback after delete or type change).
    /// </summary>
    public async Task PromoteNextPrimaryAsync(
        Guid companyId,
        Guid personId,
        ContactType contactType,
        Guid excludeContactId,
        CancellationToken cancellationToken)
    {
        var nextPrimary = await personContactRepository.GetMostRecentActiveByTypeAsync(
            companyId,
            personId,
            contactType,
            excludeContactId,
            cancellationToken);

        if (nextPrimary is null)
        {
            return;
        }

        nextPrimary.SetAsPrimary();
        await personContactRepository.UpdateAsync(nextPrimary);
    }
}
