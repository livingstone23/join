using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Regions.Commands;

/// <summary>
/// Command to register a new region in the geographic catalog.
/// </summary>
public record CreateRegionCommand : IRequest<Response<RegionDto>>
{
    /// <summary>
    /// Gets or sets the region display name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional region code.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Gets or sets the parent country identifier.
    /// </summary>
    public Guid CountryId { get; init; }
}
