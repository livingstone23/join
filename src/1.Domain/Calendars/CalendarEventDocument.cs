using JOIN.Domain.Audit;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;

namespace JOIN.Domain.Calendars;

/// <summary>
/// SPEC 69 — a document attached to a calendar activity, optionally to one of its comments (regla 2). Same
/// storage fields as <c>LeadDocument</c>; the file stays on disk while the row exists (logical delete on
/// the row only). Restrictions are enforced by <see cref="CalendarAttachmentSettings"/>.
/// </summary>
public class CalendarEventDocument : BaseTenantEntity
{
    /// <summary> Maximum characters allowed for file names. </summary>
    public const int MaxNameLength = 150;

    /// <summary> Maximum characters allowed for the stored path. </summary>
    public const int MaxPathLength = 500;

    public Guid CalendarEventId { get; set; }

    /// <summary> Null when the document is attached to the activity directly, not to one of its comments. </summary>
    public Guid? CalendarEventCommentId { get; set; }

    public DocumentType DocumentType { get; set; }

    public string OriginalName { get; set; } = string.Empty;

    public string NewName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public StorageProviderKind StorageProvider { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public Guid UploadedByUserId { get; set; }

    /// <summary> User who soft-deleted the document; null when it is still live. </summary>
    public Guid? DeletedByUserId { get; set; }

    public DateTime? DeletedUtc { get; set; }

    /// <summary> Required at delete; capped at <see cref="CalendarEventComment.MaxDeleteReasonLength"/>. </summary>
    public string? DeleteReason { get; set; }

    // --- Navigation ---
    public virtual CalendarEvent CalendarEvent { get; set; } = null!;
    public virtual CalendarEventComment? CalendarEventComment { get; set; }
    public virtual ApplicationUser UploadedByUser { get; set; } = null!;
    public virtual ApplicationUser? DeletedByUser { get; set; }
}