using System;



namespace JOIN.Application.DTO.Admin;



/// <summary>
/// Data Transfer Object (DTO) representing a customer in paginated list responses.
/// </summary>
public record PersonListItemDto
{
    /// <summary>
    /// Gets the unique identifier of the customer.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the unique identifier of the company that owns the customer.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets the display name of the company that owns the customer.
    /// </summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the customer type exposed as a string value.
    /// </summary>
    public string PersonType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the human-readable name of the customer type.
    /// </summary>
    public string PersonTypeName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the gender catalog identifier when the person is a natural person.
    /// </summary>
    public Guid? GenderId { get; init; }

    /// <summary>
    /// Gets the display name of the linked gender catalog entry.
    /// </summary>
    public string? GenderName { get; init; }

    /// <summary>
    /// Gets whether the person record is active in the system.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the first name of the customer.
    /// </summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the middle name of the customer.
    /// </summary>
    public string? MiddleName { get; init; }

    /// <summary>
    /// Gets the first surname of the customer.
    /// </summary>
    public string? LastName { get; init; }

    /// <summary>
    /// Gets the second surname of the customer.
    /// </summary>
    public string? SecondLastName { get; init; }

    /// <summary>
    /// Gets the commercial name of the customer.
    /// </summary>
    public string? CommercialName { get; init; }

    /// <summary>
    /// Gets the identification type identifier.
    /// </summary>
    public Guid IdentificationTypeId { get; init; }

    /// <summary>
    /// Gets the identification type name.
    /// </summary>
    public string? IdentificationTypeName { get; init; }

    /// <summary>
    /// Gets the identification number.
    /// </summary>
    public string IdentificationNumber { get; init; } = string.Empty;
}