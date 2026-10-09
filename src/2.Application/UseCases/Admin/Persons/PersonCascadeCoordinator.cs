// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Linq.Expressions;
using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;

namespace JOIN.Application.UseCases.Admin.Persons;

/// <summary>
/// Soft-delete cascade of a <see cref="Person"/> aggregate (SPEC 41, decision 2026-10-08).
/// <list type="bullet">
/// <item><b>Composition</b> children (addresses, contacts, employments, business and financial profiles) are
/// deleted with the person, with the same <c>GcRecord</c> stamp, and restored with it.</item>
/// <item><b>References</b> with a life of their own (customers, tickets, user links) block the delete.</item>
/// </list>
/// </summary>
public sealed class PersonCascadeCoordinator(IUnitOfWork unitOfWork, SoftDeleteRestorer restorer)
{
    /// <summary>
    /// Returns one detail per active reference type that blocks deleting the person, e.g. <c>"Active customers: 1"</c>.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetBlockingReferencesAsync(Guid personId)
    {
        var references = new ActiveDependentsCheck(unitOfWork);
        await references.CountAsync<Customer>(c => c.GcRecord == 0 && c.PersonId == personId, "customers");
        await references.CountAsync<Ticket>(t => t.GcRecord == 0 && t.PersonId == personId, "tickets");
        await references.CountAsync<UserPerson>(u => u.GcRecord == 0 && u.PersonId == personId, "user persons");
        return references.Details;
    }

    /// <summary>
    /// Marks the active composition children of <paramref name="person"/> as deleted with the same stamp as the
    /// person. Addresses and contacts come tracked with the aggregate; the other children are loaded here.
    /// </summary>
    public async Task MarkChildrenAsDeletedAsync(Person person, DateTime deletedAtUtc)
    {
        foreach (var address in person.Addresses.Where(a => a.GcRecord == BaseAuditableEntity.ActiveGcRecord))
        {
            address.MarkAsDeleted(deletedAtUtc);
        }

        foreach (var contact in person.Contacts.Where(c => c.GcRecord == BaseAuditableEntity.ActiveGcRecord))
        {
            contact.MarkAsDeleted(deletedAtUtc);
        }

        var personId = person.Id;
        await MarkAsync<PersonEmployment>(e => e.GcRecord == 0 && e.PersonId == personId, deletedAtUtc);
        await MarkAsync<PersonBusinessProfile>(p => p.GcRecord == 0 && p.PersonId == personId, deletedAtUtc);
        await MarkAsync<PersonFinancialProfile>(p => p.GcRecord == 0 && p.PersonId == personId, deletedAtUtc);
    }

    /// <summary>
    /// Restores the composition children deleted in the same cascade as the person (<c>GcRecord == stamp</c>).
    /// Every child is validated first — catalog parents active (<c>PARENT_DELETED</c>) and no active duplicate
    /// contact (<c>ACTIVE_DUPLICATE_EXISTS</c>); on any error nothing is restored. A child flagged as
    /// default/primary/current/active comes back without the flag when another active row already holds it.
    /// </summary>
    /// <returns><c>null</c> on success, or the error that aborts the person restore.</returns>
    public async Task<Response<Guid>?> RestoreChildrenAsync(Guid personId, int stamp)
    {
        var addresses = await LoadAsync<PersonAddress>(a => a.PersonId == personId && a.GcRecord == stamp);
        var contacts = await LoadAsync<PersonContact>(c => c.PersonId == personId && c.GcRecord == stamp);
        var employments = await LoadAsync<PersonEmployment>(e => e.PersonId == personId && e.GcRecord == stamp);
        var businessProfiles = await LoadAsync<PersonBusinessProfile>(p => p.PersonId == personId && p.GcRecord == stamp);
        var financialProfiles = await LoadAsync<PersonFinancialProfile>(p => p.PersonId == personId && p.GcRecord == stamp);

        var parentErrors = new List<string>();
        foreach (var address in addresses)
        {
            if (await restorer.IsParentDeletedAsync<Country>(address.CountryId)
                || await restorer.IsParentDeletedAsync<Province>(address.ProvinceId)
                || await restorer.IsParentDeletedAsync<Municipality>(address.MunicipalityId)
                || await restorer.IsParentDeletedAsync<StreetType>(address.StreetTypeId)
                || (address.RegionId is { } regionId && await restorer.IsParentDeletedAsync<Region>(regionId)))
            {
                parentErrors.Add($"PersonAddress '{address.Id}' references a deleted catalog.");
            }
        }

        foreach (var profile in businessProfiles)
        {
            if (await restorer.IsParentDeletedAsync<Industry>(profile.IndustryId)
                || await restorer.IsParentDeletedAsync<TaxRegime>(profile.TaxRegimeId))
            {
                parentErrors.Add($"PersonBusinessProfile '{profile.Id}' references a deleted catalog.");
            }
        }

        foreach (var profile in financialProfiles)
        {
            if (await restorer.IsParentDeletedAsync<IncomeRange>(profile.IncomeRangeId))
            {
                parentErrors.Add($"PersonFinancialProfile '{profile.Id}' references a deleted catalog.");
            }
        }

        if (parentErrors.Count > 0)
        {
            return Response<Guid>.Error("PARENT_DELETED", parentErrors);
        }

        var duplicateErrors = new List<string>();
        var restoredContactKeys = new HashSet<(ContactType, string)>();
        foreach (var contact in contacts)
        {
            var key = (contact.ContactType, contact.ContactValue);
            if (!restoredContactKeys.Add(key)
                || await restorer.AnyAsync<PersonContact>(c => c.GcRecord == 0 && c.PersonId == personId
                    && c.ContactType == contact.ContactType && c.ContactValue == contact.ContactValue))
            {
                duplicateErrors.Add($"PersonContact '{contact.Id}' ({contact.ContactValue}) already exists as an active contact.");
            }
        }

        if (duplicateErrors.Count > 0)
        {
            return Response<Guid>.Error("ACTIVE_DUPLICATE_EXISTS", duplicateErrors);
        }

        // Flags: one holder per person (per contact type for contacts), counting the rows restored in this batch.
        var hasDefaultAddress = await restorer.AnyAsync<PersonAddress>(a => a.GcRecord == 0 && a.PersonId == personId && a.IsDefault);
        foreach (var address in addresses)
        {
            if (address.IsDefault && hasDefaultAddress)
            {
                address.RemoveDefault();
            }

            hasDefaultAddress |= address.IsDefault;
        }

        var primaryContactTypes = new HashSet<ContactType>();
        foreach (var contact in contacts)
        {
            var type = contact.ContactType;
            if (contact.IsPrimary
                && (primaryContactTypes.Contains(type)
                    || await restorer.AnyAsync<PersonContact>(c => c.GcRecord == 0 && c.PersonId == personId && c.ContactType == type && c.IsPrimary)))
            {
                contact.RemovePrimary();
            }

            if (contact.IsPrimary)
            {
                primaryContactTypes.Add(type);
            }
        }

        var hasCurrentEmployment = await restorer.AnyAsync<PersonEmployment>(e => e.GcRecord == 0 && e.PersonId == personId && e.IsCurrent);
        foreach (var employment in employments)
        {
            if (employment.IsCurrent && hasCurrentEmployment)
            {
                employment.RemoveCurrent();
            }

            hasCurrentEmployment |= employment.IsCurrent;
        }

        var hasActiveBusinessProfile = await restorer.AnyAsync<PersonBusinessProfile>(p => p.GcRecord == 0 && p.PersonId == personId && p.IsActive);
        foreach (var profile in businessProfiles)
        {
            if (profile.IsActive && hasActiveBusinessProfile)
            {
                profile.Deactivate();
            }

            hasActiveBusinessProfile |= profile.IsActive;
        }

        var hasCurrentFinancialProfile = await restorer.AnyAsync<PersonFinancialProfile>(p => p.GcRecord == 0 && p.PersonId == personId && p.IsCurrent);
        foreach (var profile in financialProfiles)
        {
            if (profile.IsCurrent && hasCurrentFinancialProfile)
            {
                profile.Archive();
            }

            hasCurrentFinancialProfile |= profile.IsCurrent;
        }

        await RestoreAsync(addresses);
        await RestoreAsync(contacts);
        await RestoreAsync(employments);
        await RestoreAsync(businessProfiles);
        await RestoreAsync(financialProfiles);
        return null;
    }

    private async Task<IReadOnlyList<T>> LoadAsync<T>(Expression<Func<T, bool>> predicate)
        where T : class
        => (await unitOfWork.GetRepository<T>().GetAllIncludingDeletedAsync(predicate)).ToList();

    private async Task MarkAsync<T>(Expression<Func<T, bool>> predicate, DateTime deletedAtUtc)
        where T : BaseAuditableEntity
    {
        var repository = unitOfWork.GetRepository<T>();
        foreach (var row in await repository.GetAllIncludingDeletedAsync(predicate))
        {
            row.MarkAsDeleted(deletedAtUtc);
            await repository.UpdateAsync(row);
        }
    }

    private async Task RestoreAsync<T>(IEnumerable<T> rows)
        where T : BaseAuditableEntity
    {
        var repository = unitOfWork.GetRepository<T>();
        foreach (var row in rows)
        {
            row.Restore();
            await repository.UpdateAsync(row);
        }
    }
}
