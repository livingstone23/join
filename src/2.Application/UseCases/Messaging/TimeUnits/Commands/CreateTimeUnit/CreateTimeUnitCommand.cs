using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Commands;

/// <summary>
/// Command to register a new time unit in the messaging catalog.
/// </summary>
public record CreateTimeUnitCommand : IRequest<Response<TimeUnitDto>>
{
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
