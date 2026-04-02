using System;

namespace JOIN.Application.DTO.Common;

/// <summary>
/// Data Transfer Object (DTO) representing a country catalog item.
/// </summary>
public record CountryDto
{
    /// <summary>
    /// Gets the unique identifier of the country.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the country display name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the ISO code of the country.
    /// </summary>
    public string IsoCode { get; init; } = string.Empty;
}
