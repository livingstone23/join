using System;

namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data Transfer Object (DTO) representing a customer address.
/// </summary>
public record CustomerAddressDto
{
    /// <summary>
    /// Global unique identifier for the address.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Primary address line.
    /// </summary>
    public string AddressLine1 { get; init; } = string.Empty;

    /// <summary>
    /// Secondary address line.
    /// </summary>
    public string? AddressLine2 { get; init; }

    /// <summary>
    /// Postal code.
    /// </summary>
    public string ZipCode { get; init; } = string.Empty;

    /// <summary>
    /// Foreign key to street type catalog.
    /// </summary>
    public Guid StreetTypeId { get; init; }

    /// <summary>
    /// Name of the street type associated with <see cref="StreetTypeId"/>.
    /// </summary>
    public string? StreetTypeName { get; init; }

    /// <summary>
    /// Foreign key to country catalog.
    /// </summary>
    public Guid CountryId { get; init; }

    /// <summary>
    /// Name of the country associated with <see cref="CountryId"/>.
    /// </summary>
    public string? CountryName { get; init; }

    /// <summary>
    /// Foreign key to region catalog.
    /// </summary>
    public Guid? RegionId { get; init; }

    /// <summary>
    /// Name of the region associated with <see cref="RegionId"/>.
    /// Null when the address is not linked to a region.
    /// </summary>
    public string? RegionName { get; init; }

    /// <summary>
    /// Foreign key to province catalog.
    /// </summary>
    public Guid ProvinceId { get; init; }

    /// <summary>
    /// Name of the province associated with <see cref="ProvinceId"/>.
    /// </summary>
    public string? ProvinceName { get; init; }

    /// <summary>
    /// Foreign key to municipality catalog.
    /// </summary>
    public Guid MunicipalityId { get; init; }

    /// <summary>
    /// Name of the municipality associated with <see cref="MunicipalityId"/>.
    /// </summary>
    public string? MunicipalityName { get; init; }

    /// <summary>
    /// Indicates whether this is the default address for the customer.
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// Address creation timestamp formatted as yyyy-MM-dd HH:mm.
    /// </summary>
    public string CreatedAt { get; init; } = string.Empty;
}