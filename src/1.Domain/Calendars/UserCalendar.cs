using JOIN.Domain.Audit;
using JOIN.Domain.Security;

namespace JOIN.Domain.Calendars;

/// <summary>
/// A user's calendar (table <c>Calendar.UserCalendars</c>). The default calendar, or one with future
/// non-cancelled activities, cannot be deactivated or deleted (<c>CALENDAR_IS_DEFAULT</c>,
/// <c>CALENDAR_HAS_ACTIVE_EVENTS</c>).
/// </summary>
public class UserCalendar : BaseTenantEntity
{
    /// <summary> Owner. Immutable. </summary>
    public Guid OwnerUserId { get; set; }

    /// <summary> Unique per owner and company among active rows. </summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary> Color of its activities when several calendars are shown together. </summary>
    public string Color { get; set; } = string.Empty;

    /// <summary> An inactive calendar receives no new activities and is hidden from the default view. </summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    // --- Navigation ---
    public virtual ApplicationUser OwnerUser { get; set; } = null!;
    public virtual ICollection<CalendarEvent> Events { get; set; } = new List<CalendarEvent>();
}
