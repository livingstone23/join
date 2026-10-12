namespace JOIN.Domain.Calendars.Enums;

/// <summary>
/// Days of the week a user works in a company (<see cref="CalendarUserConfiguration.WorkDays"/>).
/// Flags: a value combines several days. Default Monday to Friday.
/// </summary>
[Flags]
public enum CalendarWorkDays
{
    None = 0,
    Sunday = 1,
    Monday = 2,
    Tuesday = 4,
    Wednesday = 8,
    Thursday = 16,
    Friday = 32,
    Saturday = 64,

    /// <summary> Monday to Friday. </summary>
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,

    /// <summary> Every day of the week. </summary>
    All = Sunday | Weekdays | Saturday
}
