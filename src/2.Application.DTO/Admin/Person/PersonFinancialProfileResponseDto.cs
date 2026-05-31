namespace JOIN.Application.DTO.Admin;



/// <summary>
/// Data transfer object representing a person financial profile payload exposed by Application queries.
/// </summary>
public sealed record PersonFinancialProfileResponseDto
{
    /// <summary>
    /// Gets the unique identifier of the person financial profile record.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the unique identifier of the owner person.
    /// </summary>
    public Guid PersonId { get; init; }

    /// <summary>
    /// Gets the income range catalog identifier.
    /// </summary>
    public Guid IncomeRangeId { get; init; }

    /// <summary>
    /// Gets the name of the linked income range catalog entry.
    /// </summary>
    public string IncomeRangeName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the source of funds description.
    /// </summary>
    public string SourceOfFunds { get; init; } = string.Empty;

    /// <summary>
    /// Gets the date when the financial information was declared.
    /// </summary>
    public DateTime DeclaredDate { get; init; }

    /// <summary>
    /// Gets whether this is the most recent and valid financial profile.
    /// </summary>
    public bool IsCurrent { get; init; }

    /// <summary>
    /// Gets whether this financial profile record is active in the system.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the financial profile record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
