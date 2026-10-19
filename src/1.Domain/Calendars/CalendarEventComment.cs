using JOIN.Domain.Audit;
using JOIN.Domain.Security;

namespace JOIN.Domain.Calendars;

/// <summary>
/// SPEC 69 — a comment on a calendar activity (table <c>Calendar.EventComments</c>). Comments live on the
/// master of a series (rule 1) and on the modified occurrence of an exception. Immutable for the author; only
/// an administrator deletes it (logical delete, with a required reason stored in <see cref="DeleteReason"/>).
/// The body stops being returned by the API after delete; the row stays for the bitácora.
/// </summary>
public class CalendarEventComment : BaseTenantEntity
{
    /// <summary> Maximum characters allowed for the body (regla 4 / validación 400). </summary>
    public const int MaxBodyLength = 2000;

    /// <summary> Maximum characters allowed for the delete reason. </summary>
    public const int MaxDeleteReasonLength = 500;

    public Guid CalendarEventId { get; set; }

    /// <summary> Comment text; trimmed and capped at <see cref="MaxBodyLength"/>. </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary> User who wrote the comment. </summary>
    public Guid AuthorUserId { get; set; }

    /// <summary> Server clock when the comment was recorded (UTC). </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary> User who soft-deleted the comment; null when it is still live. </summary>
    public Guid? DeletedByUserId { get; set; }

    /// <summary> UTC moment of the delete. </summary>
    public DateTime? DeletedUtc { get; set; }

    /// <summary> Required at delete; capped at <see cref="MaxDeleteReasonLength"/>. </summary>
    public string? DeleteReason { get; set; }

    // --- Navigation ---
    public virtual CalendarEvent CalendarEvent { get; set; } = null!;
    public virtual ApplicationUser AuthorUser { get; set; } = null!;
    public virtual ApplicationUser? DeletedByUser { get; set; }
    public virtual ICollection<CalendarEventDocument> Documents { get; set; } = new List<CalendarEventDocument>();
}