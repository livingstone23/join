namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data Transfer Object (DTO) representing a tenant-scoped industry catalog entry.
/// </summary>
public sealed record IndustryDto
{
    /// <summary>
    /// Gets the unique identifier of the industry.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the tenant identifier that owns the industry.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets the display name of the company that owns the industry.
    /// </summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the standard or internal code for the industry.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Gets the display name of the industry.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional description of the industry.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets a value indicating whether the industry is active for new selections.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp of the industry.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
