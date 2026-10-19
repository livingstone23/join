namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// SPEC 69 — one document attached to a calendar activity. After delete the row stays in the table but the
/// API no longer returns it for download (rule 4); the file stays on disk.
/// </summary>
public record CalendarEventDocumentDto
{
    public Guid Id { get; init; }
    public Guid CalendarEventId { get; init; }
    public Guid? CalendarEventCommentId { get; init; }
    public int DocumentType { get; init; }
    public string OriginalName { get; init; } = string.Empty;
    public string NewName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public Guid UploadedByUserId { get; init; }
    public string UploadedByUserName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public bool IsDeleted { get; init; }
}