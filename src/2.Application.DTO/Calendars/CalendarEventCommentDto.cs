namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// SPEC 69 — one comment on a calendar activity. After delete the API returns the row with
/// <see cref="Body"/> replaced by <c>null</c> and the soft-delete metadata filled in (rule 4).
/// </summary>
public record CalendarEventCommentDto
{
    public Guid Id { get; init; }
    public Guid CalendarEventId { get; init; }
    public Guid AuthorUserId { get; init; }
    public string AuthorUserName { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; }

    /// <summary> Body of the comment, or <c>null</c> when the comment has been soft-deleted. </summary>
    public string? Body { get; init; }

    /// <summary> Documents attached to this comment (may be empty). </summary>
    public IReadOnlyList<CalendarEventDocumentDto> Documents { get; init; } = [];

    public bool IsDeleted { get; init; }
    public Guid? DeletedByUserId { get; init; }
    public string? DeletedByUserName { get; init; }
    public DateTime? DeletedUtc { get; init; }
    public string? DeleteReason { get; init; }
}