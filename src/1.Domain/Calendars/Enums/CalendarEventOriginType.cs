namespace JOIN.Domain.Calendars.Enums;

/// <summary>
/// Who requested a <see cref="CalendarEvent"/> (<see cref="CalendarEvent.OriginType"/>).
/// </summary>
public enum CalendarEventOriginType
{
    /// <summary> A JOIN user. </summary>
    User = 1,

    /// <summary> A phone number or WhatsApp. </summary>
    Phone = 2,

    /// <summary> An e-mail address. </summary>
    Email = 3,

    /// <summary> An internal process. </summary>
    System = 4
}
