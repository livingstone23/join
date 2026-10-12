using JOIN.Domain.Audit;

namespace JOIN.Domain.Calendars;

/// <summary>
/// Global catalog of activity types (table <c>Calendar.Activities</c>), administered by the SuperAdmin.
/// No activity points here directly: it points to <see cref="CalendarActivityCompany"/>. These are the base
/// values copied when a company enables the type.
/// </summary>
public class CalendarActivity : BaseAuditableEntity
{
    /// <summary> Immutable technical code (<see cref="CalendarCodes.Activities"/>). Unique among active rows. </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary> Base display name. </summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary> Base hex color (<c>#1E88E5</c>). </summary>
    public string Color { get; set; } = string.Empty;

    /// <summary> Icon for the frontend. </summary>
    public string? Icon { get; set; }

    /// <summary> Duration proposed when creating an activity (5–1440 minutes). </summary>
    public int DefaultDurationMinutes { get; set; }

    /// <summary>
    /// <c>true</c> = may overlap any activity (reminder). <c>false</c> = cannot overlap another activity that
    /// does not allow it either (meeting).
    /// </summary>
    public bool AllowsOverlap { get; set; }

    /// <summary>
    /// <c>true</c> = only within the owner's business hours and work days, unless the owner creates it.
    /// </summary>
    public bool RequiresBusinessHours { get; set; }

    /// <summary> An inactive type cannot be used in any company even if enabled there. </summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    // --- Navigation ---
    public virtual ICollection<CalendarActivityCompany> ActivityCompanies { get; set; } = new List<CalendarActivityCompany>();
}
