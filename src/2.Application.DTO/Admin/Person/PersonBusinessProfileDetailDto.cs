namespace JOIN.Application.DTO.Admin;



/// <summary>
/// Business profile item embedded in a person detail response (without redundant person identifier).
/// </summary>
public sealed record PersonBusinessProfileDetailDto
{
    /// <summary>
    /// Gets the unique identifier of the person business profile record.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the industry catalog identifier.
    /// </summary>
    public Guid IndustryId { get; init; }

    /// <summary>
    /// Gets the display name of the linked industry catalog entry.
    /// </summary>
    public string IndustryName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tax regime catalog identifier.
    /// </summary>
    public Guid TaxRegimeId { get; init; }

    /// <summary>
    /// Gets the display name of the linked tax regime catalog entry.
    /// </summary>
    public string TaxRegimeName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the official corporate website of the business.
    /// </summary>
    public string? Website { get; init; }

    /// <summary>
    /// Gets the date the company was legally founded or registered.
    /// </summary>
    public DateTime? FoundationDate { get; init; }

    /// <summary>
    /// Gets whether this business profile record is active in the system.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the business profile record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
