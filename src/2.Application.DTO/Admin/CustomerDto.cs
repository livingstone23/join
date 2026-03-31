using System;
using System.Collections.Generic;



namespace JOIN.Application.DTO.Admin;



/// <summary>
/// Data Transfer Object (DTO) representing a customer.
/// Uses 'record' to guarantee immutability, thread safety, and value-based equality.
/// </summary>
public record CustomerDto
{
    /// <summary>
    /// Global unique identifier for the customer.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the unique identifier of the Company that owns this customer[cite: 915].
    /// Essential for the Global Query Filter (Multi-tenancy)[cite: 916].
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Categorizes the customer as Physical (Natural Person) or Legal (Company/Organization)[cite: 917].
    /// Exposed as a string to facilitate consumption by HTTP/JSON clients.
    /// </summary>
    public string PersonType { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the first name of the customer[cite: 918]. 
    /// Mandatory for Physical persons[cite: 918].
    /// </summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the middle name of the customer.
    /// </summary>
    public string? MiddleName { get; init; }

    /// <summary>
    /// Gets or sets the first surname of the customer[cite: 921].
    /// </summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the second surname of the customer.
    /// </summary>
    public string? SecondLastName { get; init; }

    /// <summary>
    /// Gets or sets the business or trade name.
    /// Mandatory for Legal persons.
    /// </summary>
    public string? CommercialName { get; init; }

    /// <summary>
    /// Gets or sets the foreign key for the identification document type.
    /// </summary>
    public Guid IdentificationTypeId { get; init; }

    /// <summary>
    /// Gets or sets the name of the identification type linked to <see cref="IdentificationTypeId"/>.
    /// </summary>
    public string? IdentificationTypeName { get; init; }

    /// <summary>
    /// Gets or sets the unique identification number (ID Card, Tax ID)[cite: 926].
    /// </summary>
    public string IdentificationNumber { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the addresses linked to this customer.
    /// Returns null when the customer has no address records.
    /// </summary>
    public IReadOnlyCollection<CustomerAddressDto>? Addresses { get; init; }

}