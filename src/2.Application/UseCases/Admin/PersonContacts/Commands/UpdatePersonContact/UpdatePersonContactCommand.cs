using JOIN.Application.Common;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonContacts.Commands;

/// <summary>
/// Command that updates an existing customer contact in the current tenant context.
/// </summary>
public sealed record UpdatePersonContactCommand : IRequest<Response<Guid>>
{
    /// <summary>
    /// Gets the unique identifier of the contact to update.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the unique identifier of the customer that owns the contact.
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
    /// Gets a value indicating whether this is the primary contact for the customer.
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// Gets optional administrative notes about this contact.
    /// </summary>
    public string? Comments { get; init; }
}
