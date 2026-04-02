using System;

namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data Transfer Object (DTO) used to create a customer.
/// Company ownership is resolved from the authenticated tenant context, not from request body.
/// </summary>
public record CreateCustomerDto
{
    /// <summary>
    /// Categorizes the customer as Physical (Natural Person) or Legal (Company/Organization).
    /// Exposed as a string to facilitate HTTP/JSON clients.
    /// </summary>
    public string PersonType { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the first name of the customer.
    /// </summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the middle name of the customer.
    /// </summary>
    public string? MiddleName { get; init; }

    /// <summary>
    /// Gets or sets the first surname of the customer.
    /// </summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the second surname of the customer.
    /// </summary>
    public string? SecondLastName { get; init; }

    /// <summary>
    /// Gets or sets the business or trade name.
    /// </summary>
    public string? CommercialName { get; init; }

    /// <summary>
    /// Gets or sets the foreign key for the identification document type.
    /// </summary>
    public Guid IdentificationTypeId { get; init; }

    /// <summary>
    /// Gets or sets the unique identification number (ID Card, Tax ID).
    /// </summary>
    public string IdentificationNumber { get; init; } = string.Empty;
}
