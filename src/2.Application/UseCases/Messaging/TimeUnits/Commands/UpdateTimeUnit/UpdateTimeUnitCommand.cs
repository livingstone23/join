using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Commands;

/// <summary>
/// Command to update an existing time unit catalog item.
/// </summary>
public record UpdateTimeUnitCommand : IRequest<Response<TimeUnitDto>>
{
    /// <summary>
    /// Gets the time unit identifier to update.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the display name of the time unit.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the numeric code or multiplier of the time unit.
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// Gets or sets whether the time unit is active.
    /// </summary>
    public bool IsActive { get; init; } = true;
}
