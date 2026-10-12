using JOIN.Domain.Audit;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Security;

namespace JOIN.Domain.Calendars;

/// <summary>
/// Non-working period of a user in a company (table <c>Calendar.UserNonWorkingPeriods</c>): vacation, leave,
/// absence. Whole days only. Two periods of the same user cannot overlap
/// (<c>CALENDAR_NON_WORKING_PERIOD_OVERLAP</c>). Registering one does not touch existing activities.
/// </summary>
public class CalendarUserNonWorkingPeriod : BaseTenantEntity
{
    public Guid UserId { get; set; }

    /// <summary> First non-working day (inclusive). </summary>
    public DateOnly StartDate { get; set; }

    /// <summary> Last non-working day (inclusive). <c>&gt;= StartDate</c>, at most 366 days. </summary>
    public DateOnly EndDate { get; set; }

    public CalendarNonWorkingReason Reason { get; set; }

    public string? Notes { get; set; }

    // --- Navigation ---
    public virtual ApplicationUser User { get; set; } = null!;
}
