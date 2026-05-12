namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data transfer object representing a customer address payload exposed by Application queries and commands.
/// </summary>
public sealed record CustomerAddressResponseDto
{
    /// <summary>
    /// Gets the unique identifier of the customer address.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the unique identifier of the owner customer.
    /// </summary>
    public Guid CustomerId { get; init; }

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
    /// Gets the country identifier.
    /// </summary>
    public Guid CountryId { get; init; }

    /// <summary>
    /// Gets the region identifier.
    /// </summary>
    public Guid? RegionId { get; init; }

    /// <summary>
    /// Gets the province identifier.
    /// </summary>
    public Guid ProvinceId { get; init; }

    /// <summary>
    /// Gets the municipality identifier.
    /// </summary>
    public Guid MunicipalityId { get; init; }

    /// <summary>
    /// Gets whether the address is the default one for the customer.
    /// </summary>
    public bool IsDefault { get; init; }
}
