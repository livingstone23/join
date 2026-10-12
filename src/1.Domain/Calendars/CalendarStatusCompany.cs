using JOIN.Domain.Audit;

namespace JOIN.Domain.Calendars;

/// <summary>
/// Status enabled in a company (table <c>Calendar.StatusCompanies</c>).
/// <para>
/// "Usable" rule: this row and its <see cref="CalendarStatus"/> are both active and not deleted
/// (<c>CALENDAR_STATUS_NOT_ENABLED</c>). It cannot be deactivated or deleted while it is the initial status of
/// an activity type, the company's reschedule status, or has activities; <c>PENDING</c> and
/// <c>CANCELLED</c> can never be deactivated.
/// </para>
/// </summary>
public class CalendarStatusCompany : BaseTenantEntity
{
    /// <summary> Global source status. Immutable. </summary>
    public Guid CalendarStatusId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    // --- Navigation ---
    public virtual CalendarStatus CalendarStatus { get; set; } = null!;
}
