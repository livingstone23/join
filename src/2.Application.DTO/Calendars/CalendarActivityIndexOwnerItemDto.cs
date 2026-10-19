namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// One row of the owners list of the activity index filter (<c>GET /CalendarActivityIndex/owners</c>): a
/// member of the company with a calendar (SPEC 68 C.6). The full name is trimmed and the default calendar id is
/// the calendar the index filter maps to by default.
/// </summary>
public record CalendarActivityIndexOwnerItemDto
{
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public Guid DefaultCalendarId { get; init; }
    public string DefaultCalendarName { get; init; } = string.Empty;
}