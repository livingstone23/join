using JOIN.Domain.Admin;

namespace JOIN.Application.UseCases.Admin.Persons;

/// <summary>
/// SPEC 58: identification is optional on a person. Both fields are stored together or not at all, and the number
/// is trimmed, so the filtered unique index (<c>"identificationnumber" IS NOT NULL</c>) only sees real values.
/// </summary>
public static class PersonIdentification
{
    /// <summary>Trims the number and clears both fields when either is missing.</summary>
    public static void Normalize(Person person)
    {
        var number = string.IsNullOrWhiteSpace(person.IdentificationNumber) ? null : person.IdentificationNumber.Trim();
        if (number is null || person.IdentificationTypeId is null || person.IdentificationTypeId == Guid.Empty)
        {
            person.IdentificationTypeId = null;
            person.IdentificationNumber = null;
            return;
        }

        person.IdentificationNumber = number;
    }
}
