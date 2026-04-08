using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Provinces.Commands;

/// <summary>
/// Command to register a new province in the geographic catalog.
/// </summary>
public record CreateProvinceCommand : IRequest<Response<ProvinceDto>>
{
    /// <summary>
    /// Gets or sets the province display name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the province code.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the parent country identifier.
    /// </summary>
    public Guid CountryId { get; init; }

    /// <summary>
    /// Gets or sets the optional parent region identifier.
    /// </summary>
    public Guid? RegionId { get; init; }
}