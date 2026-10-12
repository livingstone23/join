using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.UpdateCalendarActivity;

/// <summary>
/// Updates a global activity type. The code is not editable, and companies keep their own values (no propagation).
/// </summary>
public record UpdateCalendarActivityCommand : ITransactionalCommand<Response<CalendarActivityDto>>
{
    [JsonIgnore]
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Color { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public int DefaultDurationMinutes { get; init; }
    public bool AllowsOverlap { get; init; }
    public bool RequiresBusinessHours { get; init; }
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }
}
