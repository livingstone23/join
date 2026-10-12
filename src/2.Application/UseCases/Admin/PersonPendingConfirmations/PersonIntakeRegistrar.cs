using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations;

/// <summary>
/// Writes the CRM side of a person registered by the channel agent (SPEC 45): <see cref="Person"/> with its
/// <see cref="PersonContact"/> rows, and later the <see cref="PersonPendingConfirmation"/> that points at the review
/// activity. It lives in <c>Admin</c> because the calendar module only writes calendar entities (SPEC 44 module
/// isolation); the intake handler calls it inside its own transaction.
/// </summary>
public sealed class PersonIntakeRegistrar(IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Creates the person and its contacts and saves them (the review activity validates the requester link against
    /// the database). The contact of each type is its primary.
    /// </summary>
    public async Task<Person> CreatePersonAsync(Guid companyId, PersonIntakeData data, CancellationToken cancellationToken)
    {
        var person = new Person
        {
            CompanyId = companyId,
            PersonType = data.PersonType,
            FirstName = data.FirstName.Trim(),
            LastName = Clean(data.LastName) ?? string.Empty,
            CommercialName = Clean(data.CommercialName),
            IdentificationTypeId = data.IdentificationTypeId,
            IdentificationNumber = data.IdentificationNumber.Trim(),
            GenderId = data.GenderId
        };
        await unitOfWork.GetRepository<Person>().InsertAsync(person);

        if (Clean(data.Phone) is { } phone)
        {
            var contact = PersonContact.Create(companyId, person.Id, data.PhoneContactType, phone);
            contact.SetAsPrimary();
            await unitOfWork.GetRepository<PersonContact>().InsertAsync(contact);
        }

        if (Clean(data.Email) is { } email)
        {
            var contact = PersonContact.Create(companyId, person.Id, ContactType.PrimaryEmail, email);
            contact.SetAsPrimary();
            await unitOfWork.GetRepository<PersonContact>().InsertAsync(contact);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return person;
    }

    /// <summary>
    /// Opens the pending confirmation of <paramref name="personId"/> (<c>Pending</c>) and saves it.
    /// </summary>
    public async Task<PersonPendingConfirmation> AddPendingAsync(
        Guid companyId,
        Guid personId,
        string reason,
        Guid channelId,
        string channelIdentifier,
        Guid reviewEventId,
        CancellationToken cancellationToken)
    {
        var pending = new PersonPendingConfirmation
        {
            CompanyId = companyId,
            PersonId = personId,
            Reason = reason.Trim(),
            CommunicationChannelId = channelId,
            ChannelIdentifier = channelIdentifier.Trim(),
            RequestedUtc = DateTime.UtcNow,
            Status = PersonPendingConfirmationStatus.Pending,
            ReviewEventId = reviewEventId
        };
        await unitOfWork.GetRepository<PersonPendingConfirmation>().InsertAsync(pending);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return pending;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Data of a person registered by the channel agent. <see cref="PhoneContactType"/> is <c>WhatsApp</c> when the
/// request came through WhatsApp and <c>MobilePhone</c> otherwise.
/// </summary>
public sealed record PersonIntakeData(
    PersonType PersonType,
    string FirstName,
    string? LastName,
    string? CommercialName,
    Guid IdentificationTypeId,
    string IdentificationNumber,
    Guid? GenderId,
    string? Phone,
    ContactType PhoneContactType,
    string? Email);
