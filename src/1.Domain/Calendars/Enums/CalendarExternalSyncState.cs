namespace JOIN.Domain.Calendars.Enums;

/// <summary>
/// State of the link between a JOIN <see cref="CalendarEvent"/> and its external counterpart
/// (see <see cref="CalendarExternalEventLink"/>). The conflict resolution rule (last write wins) is
/// applied by the actions spec; this enum only records what happened.
/// </summary>
public enum CalendarExternalSyncState
{
    PendingPush = 1,
    Synced = 2,
    Conflict = 3,
    Error = 4,
    RemovedExternally = 5,
    Unlinked = 6
}