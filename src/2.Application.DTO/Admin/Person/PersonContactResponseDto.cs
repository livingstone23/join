namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data transfer object representing a customer contact payload exposed by Application queries and commands.
/// </summary>
public sealed record PersonContactResponseDto
{
    /// <summary>
    /// Gets the unique identifier of the customer contact.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the unique identifier of the owner customer.
    /// </summary>
    public Guid PersonId { get; init; }

    /// <summary>
    /// Gets the contact type numeric value.
    /// </summary>
    public int ContactType { get; init; }

    /// <summary>
    /// Gets the actual contact value (phone number, email, etc.).
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

    /// <summary>
    /// Gets the tenant company identifier.
    /// </summary>
    public Guid CompanyId { get; init; }
}
