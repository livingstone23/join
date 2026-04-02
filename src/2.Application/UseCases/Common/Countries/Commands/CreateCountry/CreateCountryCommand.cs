using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Countries.Commands;

/// <summary>
/// Command to register a new country in the catalog.
/// </summary>
public record CreateCountryCommand : IRequest<Response<CountryDto>>
{
    /// <summary>
    /// Gets or sets the country name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the ISO code.
    /// </summary>
    public string IsoCode { get; init; } = string.Empty;
}
