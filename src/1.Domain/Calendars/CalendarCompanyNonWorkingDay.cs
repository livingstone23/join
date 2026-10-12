using JOIN.Domain.Audit;

namespace JOIN.Domain.Calendars;

/// <summary>
/// Company holiday (table <c>Calendar.CompanyNonWorkingDays</c>). Applies to every user of the company and
/// blocks only activities whose type requires business hours, unless the owner creates them.
/// </summary>
public class CalendarCompanyNonWorkingDay : BaseTenantEntity
{
    public DateOnly Date { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// <c>true</c> = repeats every year on the same day and month (compared by month and day in C#).
    /// Movable holidays are registered once per year with <c>false</c>.
    /// </summary>
    public bool IsRecurringYearly { get; set; }
}
