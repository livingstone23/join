using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Provinces.Commands;

/// <summary>
/// Command to update an existing province catalog item.
/// </summary>
public record UpdateProvinceCommand : IRequest<Response<ProvinceDto>>
{
    /// <summary>
    /// Gets the province identifier to update.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }

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