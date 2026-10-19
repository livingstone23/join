using JOIN.Domain.Audit;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Security;

namespace JOIN.Domain.Calendars;

/// <summary>
/// Calendar configuration of a user in a company (table <c>Calendar.UserConfigurations</c>). One row per user
/// and company: the unique index on <c>(CompanyId, UserId)</c> is what guarantees a single default calendar.
/// <para>
/// Effective business hours = the user's own if set; otherwise the company's (the whole day in a 24-hour
/// company).
/// </para>
/// </summary>
public class CalendarUserConfiguration : BaseTenantEntity
{
    public Guid UserId { get; set; }

    /// <summary>
    /// Calendar where activities are registered when no other is given (including the agent's). Must belong to
    /// the same user and company, active and not deleted.
    /// </summary>
    public Guid DefaultCalendarId { get; set; }

    /// <summary> Contract start time. <c>null</c> = uses the company's. </summary>
    public TimeOnly? BusinessHoursStart { get; set; }

    /// <summary>
    /// Contract end time. <c>null</c> = uses the company's. Both or neither; later than the start and within the
    /// company's business hours.
    /// </summary>
    public TimeOnly? BusinessHoursEnd { get; set; }

    /// <summary> Days the user works. At least one. </summary>
    public CalendarWorkDays WorkDays { get; set; } = CalendarWorkDays.Weekdays;

    public CalendarViewType DefaultView { get; set; } = CalendarViewType.Week;

    /// <summary> <see cref="DayOfWeek.Monday"/> or <see cref="DayOfWeek.Sunday"/>. </summary>
    public DayOfWeek WeekStartsOn { get; set; } = DayOfWeek.Monday;

    /// <summary>
    /// SPEC 68 — when <c>true</c>, the user sees the activities of every member of the company in the activity
    /// index (and in comments/documents, SPEC 69). Default <c>false</c>: the index lists only the user's own
    /// activities. Only an administrator can change it (<c>PUT /CalendarUserConfigurations/{userId}</c> with
    /// <c>CalendarSettings.CanUpdate</c>); <c>PUT …/me</c> silently ignores it.
    /// </summary>
    public bool CanViewOthersActivities { get; set; }

    // --- Navigation ---
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual UserCalendar DefaultCalendar { get; set; } = null!;
}
