using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Customers;

namespace JOIN.Domain.Calendars;

/// <summary>
/// A calendar activity (table <c>Calendar.Events</c>): one-time, all-day or recurring. A recurring activity
/// is a master (<see cref="RecurrenceRule"/> set); a modified or cancelled occurrence is an exception that
/// points to its master (<see cref="SeriesMasterId"/> + <see cref="OriginalStartUtc"/>) and inherits every
/// field it does not change. Every <c>*Utc</c> value is stored with <see cref="DateTimeKind.Utc"/>.
/// </summary>
public class CalendarEvent : BaseTenantEntity
{
    // --- What and when ---

    /// <summary> Calendar where it lives. Can be moved to another calendar of the SAME owner. </summary>
    public Guid CalendarId { get; set; }

    /// <summary>
    /// Owner (copy of <see cref="UserCalendar.OwnerUserId"/>), so the per-user overlap check is a single
    /// indexed query.
    /// </summary>
    public Guid OwnerUserId { get; set; }

    /// <summary> Activity type; must be usable. </summary>
    public Guid CalendarActivityCompanyId { get; set; }

    /// <summary> Current status. </summary>
    public Guid CalendarStatusCompanyId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary> Short summary (what the agent understood of the request). </summary>
    public string? Summary { get; set; }

    public string? Description { get; set; }

    public string? Location { get; set; }

    /// <summary> Video call link; a valid <c>https</c> URL. </summary>
    public string? VideoCallUrl { get; set; }

    public bool IsAllDay { get; set; }

    /// <summary>
    /// Start in UTC. In an all-day activity, local midnight (company time zone) of the first day.
    /// </summary>
    public DateTime StartUtc { get; set; }

    /// <summary>
    /// Exclusive end in UTC, later than <see cref="StartUtc"/>. In an all-day activity, local midnight of the day
    /// after the last one. An occurrence lasts at most 14 days.
    /// </summary>
    public DateTime EndUtc { get; set; }

    // --- Recurrence ---

    /// <summary>
    /// RFC 5545 rule WITHOUT the <c>RRULE:</c> prefix. <c>null</c> = one-time activity. Only <c>FREQ</c>
    /// DAILY/WEEKLY/MONTHLY/YEARLY with <c>INTERVAL</c>, <c>BYDAY</c>, <c>BYMONTHDAY</c>, <c>COUNT</c> or
    /// <c>UNTIL</c>.
    /// </summary>
    public string? RecurrenceRule { get; set; }

    /// <summary>
    /// Computed: end of the last occurrence (from <c>UNTIL</c> or <c>COUNT</c>). <c>null</c> = no end.
    /// </summary>
    public DateTime? RecurrenceEndUtc { get; set; }

    /// <summary> In an exception, the series master. <c>null</c> in one-time activities and masters. </summary>
    public Guid? SeriesMasterId { get; set; }

    /// <summary>
    /// In an exception, the start the occurrence had in the original series. Required when
    /// <see cref="SeriesMasterId"/> has a value.
    /// </summary>
    public DateTime? OriginalStartUtc { get; set; }

    // --- Origin ---

    /// <summary> Channel it was created through; <c>WEB</c> from the application. </summary>
    public Guid OriginChannelId { get; set; }

    public CalendarEventOriginType OriginType { get; set; }

    /// <summary> JOIN user who registered it (the agent's service user when it came through the agent). </summary>
    public Guid? OriginUserId { get; set; }

    /// <summary> E.164 phone or e-mail of the requester when it is not a JOIN user. </summary>
    public string? OriginIdentifier { get; set; }

    /// <summary> External id of the message or conversation; avoids duplicates on agent retries. </summary>
    public string? OriginReference { get; set; }

    // --- CRM links (optional, same company) ---

    public Guid? RequesterPersonId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? TicketId { get; set; }

    public Guid? ProjectId { get; set; }

    // --- Status ---

    public DateTime? StatusChangedUtc { get; set; }

    public Guid? StatusChangedByUserId { get; set; }

    // --- Navigation ---
    public virtual UserCalendar Calendar { get; set; } = null!;
    public virtual ApplicationUser OwnerUser { get; set; } = null!;
    public virtual CalendarActivityCompany CalendarActivityCompany { get; set; } = null!;
    public virtual CalendarStatusCompany CalendarStatusCompany { get; set; } = null!;
    public virtual CalendarEvent? SeriesMaster { get; set; }
    public virtual ICollection<CalendarEvent> Exceptions { get; set; } = new List<CalendarEvent>();
    public virtual CommunicationChannel OriginChannel { get; set; } = null!;
    public virtual ApplicationUser? OriginUser { get; set; }
    public virtual Person? RequesterPerson { get; set; }
    public virtual Customer? Customer { get; set; }
    public virtual Ticket? Ticket { get; set; }
    public virtual Project? Project { get; set; }
    public virtual ApplicationUser? StatusChangedByUser { get; set; }
    public virtual ICollection<CalendarEventLog> Logs { get; set; } = new List<CalendarEventLog>();
}
