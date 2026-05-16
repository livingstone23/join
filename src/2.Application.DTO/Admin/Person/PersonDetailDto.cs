namespace JOIN.Application.DTO.Admin;



/// <summary>
/// Aggregate read model for a person detail response including related child collections.
/// </summary>
public sealed record PersonDetailDto
{
    /// <summary>
    /// Gets the core person data aligned with paginated list item shape.
    /// </summary>
    public PersonListItemDto Person { get; init; } = null!;

    /// <summary>
    /// Gets the person addresses. Null when the person has no address records.
    /// </summary>
    public IReadOnlyCollection<PersonAddressDto>? Addresses { get; init; }

    /// <summary>
    /// Gets the person contacts. Null when the person has no contact records.
    /// </summary>
    public IReadOnlyCollection<PersonContactDto>? Contacts { get; init; }

    /// <summary>
    /// Gets the person employment history. Null when there are no employment records.
    /// </summary>
    public IReadOnlyCollection<PersonEmploymentDetailDto>? Employments { get; init; }

    /// <summary>
    /// Gets the person business profiles. Null when there are no business profile records.
    /// </summary>
    public IReadOnlyCollection<PersonBusinessProfileDetailDto>? BusinessProfiles { get; init; }

    /// <summary>
    /// Gets the person financial profiles. Null when there are no financial profile records.
    /// </summary>
    public IReadOnlyCollection<PersonFinancialProfileDetailDto>? FinancialProfiles { get; init; }
}
