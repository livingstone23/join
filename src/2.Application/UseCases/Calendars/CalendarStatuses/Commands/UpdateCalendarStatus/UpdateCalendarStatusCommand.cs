using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.UpdateCalendarStatus;

/// <summary>
/// Updates a global status. The code is not editable; system statuses cannot be deactivated.
/// </summary>
public record UpdateCalendarStatusCommand : ITransactionalCommand<Response<CalendarStatusDto>>
{
    [JsonIgnore]
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Color { get; init; } = string.Empty;
    public bool BlocksTime { get; init; } = true;
    public bool IsFinal { get; init; }
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }
}
