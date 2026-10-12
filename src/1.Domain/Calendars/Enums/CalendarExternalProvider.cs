namespace JOIN.Domain.Calendars.Enums;

/// <summary>
/// Provider of an external calendar linked to a JOIN user's account. The enum is open by design:
/// adding a new value (e.g. Outlook) does NOT require changes to the entity model.
/// </summary>
public enum CalendarExternalProvider
{
    Google = 1,

    /// <summary> Reserved for a future stage. </summary>
    Microsoft = 2
}