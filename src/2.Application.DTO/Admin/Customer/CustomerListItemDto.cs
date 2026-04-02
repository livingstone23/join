using System;



namespace JOIN.Application.DTO.Admin;



/// <summary>
/// Data Transfer Object (DTO) representing a customer in paginated list responses.
/// </summary>
public record CustomerListItemDto
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
    /// Gets the customer type exposed as a string value.
    /// </summary>
    public string PersonType { get; init; } = string.Empty;

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