using JOIN.Domain.Audit;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Security;

namespace JOIN.Domain.Calendars;

/// <summary>
/// Log of a <see cref="CalendarEvent"/> (table <c>Calendar.EventLogs</c>). One row per operation, with the
/// channel and the actor. Read-only from the API.
/// </summary>
public class CalendarEventLog : BaseTenantEntity
{
    public Guid CalendarEventId { get; set; }

    public CalendarEventLogAction Action { get; set; }

    public DateTime? PreviousStartUtc { get; set; }

    public DateTime? PreviousEndUtc { get; set; }

    public DateTime? NewStartUtc { get; set; }

    public DateTime? NewEndUtc { get; set; }

    public Guid? PreviousStatusCompanyId { get; set; }

    public Guid? NewStatusCompanyId { get; set; }

    /// <summary> Channel the change was made through. </summary>
    public Guid ChannelId { get; set; }

    /// <summary> JOIN user who made the change. </summary>
    public Guid? ActorUserId { get; set; }

    /// <summary> Phone or e-mail when an external requester asked for the change through the agent. </summary>
    public string? ActorIdentifier { get; set; }

    /// <summary> Reason; required when cancelling. </summary>
    public string? Notes { get; set; }

    /// <summary> SPEC 69 — set on a <see cref="CalendarEventLogAction.CommentAdded"/> or
    /// <see cref="CalendarEventLogAction.CommentDeleted"/> row. Null for every other action. </summary>
    public Guid? CalendarEventCommentId { get; set; }

    /// <summary> SPEC 69 — set on a <see cref="CalendarEventLogAction.DocumentAdded"/> or
    /// <see cref="CalendarEventLogAction.DocumentDeleted"/> row. Null for every other action. </summary>
    public Guid? CalendarEventDocumentId { get; set; }

    public DateTime OccurredUtc { get; set; }

    // --- Navigation ---
    public virtual CalendarEvent CalendarEvent { get; set; } = null!;
    public virtual CommunicationChannel Channel { get; set; } = null!;
    public virtual ApplicationUser? ActorUser { get; set; }
    public virtual CalendarEventComment? CalendarEventComment { get; set; }
    public virtual CalendarEventDocument? CalendarEventDocument { get; set; }
}
