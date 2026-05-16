namespace JOIN.Application.DTO.Admin;



/// <summary>
/// Data transfer object representing a person address payload exposed by Application queries and commands.
/// </summary>
public sealed record PersonAddressResponseDto
{

    /// <summary>
    /// Gets the unique identifier of the person address.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the unique identifier of the owner person.
    /// </summary>
    public Guid PersonId { get; init; }

    /// <summary>
    /// Gets the primary address line.
    /// </summary>
    public string AddressLine1 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the secondary address line.
    /// </summary>
    public string? AddressLine2 { get; init; }

    /// <summary>
    /// Gets the postal code.
    /// </summary>
    public string ZipCode { get; init; } = string.Empty;

    /// <summary>
    /// Gets the street type identifier.
    /// </summary>
    public Guid StreetTypeId { get; init; }

    /// <summary>
    /// Gets the street type display name.
    /// </summary>
    public string? StreetTypeName { get; init; }

    /// <summary>
    /// Gets the country identifier.
    /// </summary>
    public Guid CountryId { get; init; }

    /// <summary>
    /// Gets the country display name.
    /// </summary>
    public string? CountryName { get; init; }

    /// <summary>
    /// Gets the region identifier.
    /// </summary>
    public Guid? RegionId { get; init; }

    /// <summary>
    /// Gets the region display name.
    /// </summary>
    public string? RegionName { get; init; }

    /// <summary>
    /// Gets the province identifier.
    /// </summary>
    public Guid ProvinceId { get; init; }

    /// <summary>
    /// Gets the province display name.
    /// </summary>
    public string? ProvinceName { get; init; }

    /// <summary>
    /// Gets the municipality identifier.
    /// </summary>
    public Guid MunicipalityId { get; init; }

    /// <summary>
    /// Gets the municipality display name.
    /// </summary>
    public string? MunicipalityName { get; init; }

    /// <summary>
    /// Gets whether the address is the default one for the person.
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// Gets the creation timestamp formatted as yyyy-MM-dd HH:mm.
    /// </summary>
    public string CreatedAt { get; init; } = string.Empty;
}
