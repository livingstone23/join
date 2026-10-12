using JOIN.Domain.Audit;
using JOIN.Domain.Security;

namespace JOIN.Domain.Calendars;

/// <summary>
/// Calendar configuration of a company (table <c>Calendar.CalendarCompanies</c>). One row per company.
/// If the business hours change and a user's own hours fall outside the new range, the operation fails with
/// <c>CALENDAR_USER_HOURS_OUT_OF_COMPANY_RANGE</c>.
/// </summary>
public class CalendarCompany : BaseTenantEntity
{
    /// <summary>
    /// User (normally a supervisor) who RECEIVES every activity that arrives without an identified
    /// recipient, so they can attend it or redistribute it to another user. Used when:
    /// <list type="bullet">
    ///   <item>The channel agent (SPEC 45) registers a request from a NEW requester (a new service,
    ///   a ticket, a first contact) who does not ask for a specific user.</item>
    ///   <item>An existing requester gives the e-mail of the user who attends them, but that e-mail does
    ///   not match (exact, case-insensitive) an active user of this company with a calendar. The activity
    ///   is routed here directly (no error) with a note containing the e-mail the requester gave.</item>
    ///   <item>The agent registers a new <c>Person</c>: the "Review created person" activity
    ///   (<c>PERSON_REVIEW</c>) is created in this user's calendar.</item>
    /// </list>
    /// The activity always goes to this user's DEFAULT calendar (<see cref="CalendarUserConfiguration.DefaultCalendarId"/>).
    /// Must be a user with an active <c>UserCompany</c> in this company and a calendar configuration;
    /// if not, channel intake fails with <c>CALENDAR_INTAKE_TARGET_USER_HAS_NO_CALENDAR</c>.
    /// There is no lookup of users by name: a recipient is identified only by exact e-mail or falls back here.
    /// </summary>
    public Guid DefaultActivityUserId { get; set; }

    /// <summary>
    /// IANA time zone (<c>America/Managua</c>). Every time of the module is interpreted in this zone.
    /// </summary>
    public string TimeZone { get; set; } = string.Empty;

    /// <summary> <c>true</c> = the company operates 24 hours; business hours are ignored. </summary>
    public bool IsOpen24Hours { get; set; }

    /// <summary> Opening time. Required when <see cref="IsOpen24Hours"/> is <c>false</c>. </summary>
    public TimeOnly? BusinessHoursStart { get; set; }

    /// <summary>
    /// Closing time. Required when <see cref="IsOpen24Hours"/> is <c>false</c>; must be later than the opening
    /// time (no night shifts).
    /// </summary>
    public TimeOnly? BusinessHoursEnd { get; set; }

    /// <summary> Name of the calendar created automatically for each user. </summary>
    public string DefaultCalendarName { get; set; } = "Mi calendario";

    /// <summary>
    /// Company-level status (<see cref="CalendarStatusCompany"/>) that the system assigns automatically
    /// to a <see cref="CalendarEvent"/> when its start or end time is changed by someone who is NOT the
    /// owner of the calendar (another user through the calendar panel, or the channel agent of SPEC 45).
    /// Its purpose is to make the owner review and confirm the new date/time.
    /// <para>
    /// NOT applied when: the owner reschedules their own activity (the status is kept), the activity is
    /// created (the initial status comes from <see cref="CalendarActivityCompany.InitialCalendarStatusCompanyId"/>),
    /// or only non-time fields change (title, description, location...).
    /// </para>
    /// <para>
    /// Default value: the company's status whose global code is <c>RESCHEDULED</c>. Must reference an
    /// active, non-deleted status of the same company. Read it from here; never hard-code the
    /// <c>RESCHEDULED</c> code in handlers.
    /// </para>
    /// </summary>
    public Guid RescheduledByOthersStatusCompanyId { get; set; }

    // --- Navigation ---
    public virtual ApplicationUser DefaultActivityUser { get; set; } = null!;
    public virtual CalendarStatusCompany RescheduledByOthersStatusCompany { get; set; } = null!;
}
