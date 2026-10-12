namespace JOIN.Domain.Calendars.Enums;

/// <summary>
/// Reason of a user's non-working period (<see cref="CalendarUserNonWorkingPeriod.Reason"/>).
/// The reason is never revealed to other users when it blocks an activity.
/// </summary>
public enum CalendarNonWorkingReason
{
    Vacation = 1,
    Leave = 2,
    Absence = 3,
    Other = 99
}
