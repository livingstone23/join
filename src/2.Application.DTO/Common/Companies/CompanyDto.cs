using System;

namespace JOIN.Application.DTO.Common;

/// <summary>
/// Data Transfer Object (DTO) representing a company catalog item.
/// </summary>
public record CompanyDto
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
    /// Gets the company description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the company tax identifier.
    /// </summary>
    public string TaxId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the company email.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// Gets the company phone.
    /// </summary>
    public string? Phone { get; init; }

    /// <summary>
    /// Gets the company website.
    /// </summary>
    public string? WebSite { get; init; }

    /// <summary>
    /// Gets whether the company is active.
    /// </summary>
    public bool IsActive { get; init; }
}
