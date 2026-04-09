using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Regions.Commands;

/// <summary>
/// Command to update an existing region catalog item.
/// </summary>
public record UpdateRegionCommand : IRequest<Response<RegionDto>>
{
    /// <summary>
    /// Gets the region identifier to update.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }

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
