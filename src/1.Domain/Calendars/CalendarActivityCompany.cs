using JOIN.Domain.Audit;

namespace JOIN.Domain.Calendars;

/// <summary>
/// Activity type enabled in a company (table <c>Calendar.ActivityCompanies</c>), with the company's
/// effective values. Values are copied from <see cref="CalendarActivity"/> when the type is enabled and then
/// edited by the company.
/// <para>
/// "Usable" rule: a type can be used to create or modify an activity only if this row and its
/// <see cref="CalendarActivity"/> are both active and not deleted (<c>CALENDAR_ACTIVITY_NOT_ENABLED</c>).
/// </para>
/// </summary>
public class CalendarActivityCompany : BaseTenantEntity
{
    /// <summary> Global source type. Cannot be changed after creation. </summary>
    public Guid CalendarActivityId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;

    /// <summary> 5–1440 minutes. </summary>
    public int DefaultDurationMinutes { get; set; }

    public bool AllowsOverlap { get; set; }

    public bool RequiresBusinessHours { get; set; }

    /// <summary>
    /// Status every activity of this type is born with (created by a user or by the agent). Must be a usable
    /// <see cref="CalendarStatusCompany"/> of the same company.
    /// </summary>
    public Guid InitialCalendarStatusCompanyId { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    // --- Navigation ---
    public virtual CalendarActivity CalendarActivity { get; set; } = null!;
    public virtual CalendarStatusCompany InitialCalendarStatusCompany { get; set; } = null!;
}
