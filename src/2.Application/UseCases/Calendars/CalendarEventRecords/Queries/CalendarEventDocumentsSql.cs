using System.Data;
using Dapper;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;

/// <summary>
/// SPEC 69 — the documents read shared by the comments list (documents nested under each comment) and the
/// documents list. Tenant and soft-delete filtered; the <c>(CalendarEventId, Created)</c> index anchors it.
/// Soft-deleted documents stay in the result with their delete metadata (rule 4).
/// </summary>
internal static class CalendarEventDocumentsSql
{
    private const string Sql = """
        SELECT d.Id, d.CalendarEventId, d.CalendarEventCommentId, d.DocumentType, d.OriginalName, d.NewName,
               d.ContentType, d.SizeBytes, d.UploadedByUserId, CONCAT(u.FirstName, ' ', u.LastName) AS UploadedByUserName,
               d.Created AS CreatedAt, d.DeletedByUserId, CONCAT(du.FirstName, ' ', du.LastName) AS DeletedByUserName,
               d.DeletedUtc, d.DeleteReason
        FROM Calendar.EventDocuments d
        LEFT JOIN Security.Users u ON u.Id = d.UploadedByUserId
        LEFT JOIN Security.Users du ON du.Id = d.DeletedByUserId
        WHERE d.CompanyId = @TenantId
          AND d.GcRecord = 0
          AND d.CalendarEventId = @EventId
        """;

    private const string CommentFilter = "\n  AND d.CalendarEventCommentId = @CommentId";

    private const string OrderBy = "\nORDER BY d.Created;";

    public static async Task<IReadOnlyList<CalendarEventDocumentDto>> ReadAsync(
        IDbConnection connection,
        Guid tenantId,
        Guid eventId,
        Guid? commentId,
        CancellationToken cancellationToken)
    {
        var sql = Sql + (commentId is null ? string.Empty : CommentFilter) + OrderBy;
        var rows = await connection.QueryAsync<DocumentRow>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, EventId = eventId, CommentId = commentId },
            cancellationToken: cancellationToken));

        return rows.Select(r => new CalendarEventDocumentDto
        {
            Id = r.Id,
            CalendarEventId = r.CalendarEventId,
            CalendarEventCommentId = r.CalendarEventCommentId,
            DocumentType = r.DocumentType,
            OriginalName = r.OriginalName,
            NewName = r.NewName,
            ContentType = r.ContentType,
            SizeBytes = r.SizeBytes,
            UploadedByUserId = r.UploadedByUserId,
            UploadedByUserName = r.UploadedByUserName?.Trim() ?? string.Empty,
            CreatedAt = r.CreatedAt,
            IsDeleted = r.DeletedUtc is not null,
            DeletedByUserId = r.DeletedByUserId,
            DeletedByUserName = NullIfBlank(r.DeletedByUserName),
            DeletedUtc = r.DeletedUtc,
            DeleteReason = r.DeleteReason
        }).ToList();
    }

    /// <summary> <c>CONCAT</c> over a missing user yields a blank string; the DTO reports it as <c>null</c>. </summary>
    internal static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class DocumentRow
    {
        public Guid Id { get; set; }
        public Guid CalendarEventId { get; set; }
        public Guid? CalendarEventCommentId { get; set; }
        public int DocumentType { get; set; }
        public string OriginalName { get; set; } = string.Empty;
        public string NewName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public Guid UploadedByUserId { get; set; }
        public string? UploadedByUserName { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? DeletedByUserId { get; set; }
        public string? DeletedByUserName { get; set; }
        public DateTime? DeletedUtc { get; set; }
        public string? DeleteReason { get; set; }
    }
}
