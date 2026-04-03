using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.StreetTypes.Commands;

/// <summary>
/// Command to update an existing street type.
/// </summary>
public record UpdateStreetTypeCommand : IRequest<Response<StreetTypeDto>>
{
    /// <summary>
    /// Gets the street type identifier to update.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the street type name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the street type abbreviation.
    /// </summary>
    public string Abbreviation { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the street type is active.
    /// </summary>
    public bool IsActive { get; init; } = true;
}
