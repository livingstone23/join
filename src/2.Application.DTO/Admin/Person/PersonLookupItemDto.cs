namespace JOIN.Application.DTO.Admin;

/// <summary>
/// One row of <c>GET /Persons/lookup</c> (SPEC 54): a person of the company, once, with its customer data when it is
/// a customer (active or inactive).
/// </summary>
public sealed record PersonLookupItemDto
{
    /// <summary>What a ticket stores.</summary>
    public Guid PersonId { get; init; }
    /// <summary><c>PersonType</c>: 1 Physical, 2 Legal.</summary>
    public int PersonType { get; init; }
    /// <summary>First, middle and last names; for a legal person, its commercial name when it has one.</summary>
    public string FullName { get; init; } = string.Empty;
    public string? CommercialName { get; init; }
    public Guid? IdentificationTypeId { get; init; }
    public string? IdentificationTypeName { get; init; }
    public string? IdentificationNumber { get; init; }
    /// <summary>The person has a customer in the company, active or inactive.</summary>
    public bool IsCustomer { get; init; }
    /// <summary><see cref="IsCustomer"/> and the customer is active.</summary>
    public bool IsCustomerActive { get; init; }
    public Guid? CustomerId { get; init; }
    public string? CustomerCode { get; init; }
    public Guid? CustomerStageId { get; init; }
    public string? CustomerStageCode { get; init; }
    public string? CustomerStageName { get; init; }
}
