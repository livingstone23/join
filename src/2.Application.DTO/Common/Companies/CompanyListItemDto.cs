using System;

namespace JOIN.Application.DTO.Common;

/// <summary>
/// Data Transfer Object (DTO) used for paginated company responses.
/// </summary>
public record CompanyListItemDto
{
    /// <summary>
    /// Gets the company identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the legal company name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the company tax identifier.
    /// </summary>
    public string TaxId { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the company is active.
    /// </summary>
    public bool IsActive { get; init; }
}
