using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Commands.UpdateUserCalendar;

/// <summary>
/// Updates a calendar of the current user.
/// </summary>
public record UpdateUserCalendarCommand : ITransactionalCommand<Response<UserCalendarDto>>
{
    [JsonIgnore]
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Color { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }
}
