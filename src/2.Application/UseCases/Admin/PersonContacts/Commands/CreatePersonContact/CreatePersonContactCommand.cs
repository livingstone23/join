using JOIN.Application.Common;
using JOIN.Domain.Enums;
using MediatR;



namespace JOIN.Application.UseCases.Admin.PersonContacts.Commands;



/// <summary>
/// Command that creates a new contact for an existing person in the current tenant context.
/// </summary>
public sealed record CreatePersonContactCommand : IRequest<Response<Guid>>
{
    /// <summary>
    /// Gets the identifier of the person owner.
    /// </summary>
    public Guid PersonId { get; init; }

    /// <summary>
    /// Gets the category of the contact.
    /// </summary>
    public ContactType ContactType { get; init; }

    /// <summary>
    /// Gets the actual contact information (phone number, email, etc.).
    /// </summary>
    public string ContactValue { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether this is the primary contact for the person.
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// Gets optional administrative notes about this contact.
    /// </summary>
    public string? Comments { get; init; }
}
