namespace JOIN.Domain.Calendars.Enums;

/// <summary>
/// Status of a <see cref="CalendarExternalConnection"/>. <c>Revoked</c> removes the row from the
/// unique-active index, so the user can re-link later.
/// </summary>
public enum CalendarExternalConnectionStatus
{
    Active = 1,
    Revoked = 2,
    Error = 3,
    Paused = 4
}