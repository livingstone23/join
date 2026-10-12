using JOIN.Domain.Audit;

namespace JOIN.Domain.Calendars;

/// <summary>
/// Global catalog of activity statuses (table <c>Calendar.Statuses</c>), administered by the SuperAdmin.
/// <see cref="BlocksTime"/> and <see cref="IsFinal"/> are not copied to the company: they define system
/// behavior and are the same for every company.
/// </summary>
public class CalendarStatus : BaseAuditableEntity
{
    /// <summary> Immutable technical code (<see cref="CalendarCodes.Statuses"/>). Unique among active rows. </summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Color { get; set; } = string.Empty;

    /// <summary> <c>true</c> = an activity in this status occupies its time slot for overlap validation. </summary>
    public bool BlocksTime { get; set; }

    /// <summary>
    /// Informative in this stage: the activity's cycle ended (filters, future Hangfire jobs). Does not prevent
    /// status changes.
    /// </summary>
    public bool IsFinal { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    // --- Navigation ---
    public virtual ICollection<CalendarStatusCompany> StatusCompanies { get; set; } = new List<CalendarStatusCompany>();
}
