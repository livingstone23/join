using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonAddresses.Commands;

/// <summary>
/// Command that creates a new address for an existing customer in the current tenant context.
/// </summary>
public sealed record CreatePersonAddressCommand : ITransactionalCommand<Response<Guid>>
{
    /// <summary>
    /// Gets the identifier of the customer owner.
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
    /// Gets whether this address should be marked as default.
    /// </summary>
    public bool IsDefault { get; init; }
}
