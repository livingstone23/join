using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Countries.Commands;

/// <summary>
/// Command to update an existing country catalog item.
/// </summary>
public record UpdateCountryCommand : IRequest<Response<CountryDto>>
{
    /// <summary>
    /// Gets the country identifier to update.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the country name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the ISO code.
    /// </summary>
    public string IsoCode { get; init; } = string.Empty;
}
