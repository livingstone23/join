using JOIN.Domain.Audit;
using JOIN.Domain.Enums;

namespace JOIN.Domain.Calendars;

/// <summary>
/// SPEC 69 — attachment limits of a company for calendar activities (regla 7, table
/// <c>Calendar.AttachmentSettings</c>, one live row per company). Without a row the upload endpoints answer
/// <c>CALENDAR_ATTACHMENT_SETTINGS_NOT_CONFIGURED</c>. Defaults match leads: PDF / Word / Excel / Image, 10 MB per
/// file, 5 per comment, 200 per day.
/// </summary>
public class CalendarAttachmentSettings : BaseTenantEntity
{
    /// <summary> Default maximum size per file in bytes (10 MB). </summary>
    public const long DefaultMaxFileSizeBytes = 10L * 1024 * 1024;

    /// <summary> Default maximum number of files per comment. </summary>
    public const int DefaultMaxFilesPerComment = 5;

    /// <summary> Default maximum number of files per company per day. </summary>
    public const int DefaultMaxFilesPerDay = 200;

    /// <summary> Default bitmask of allowed types (PDF / Word / Excel / Image). </summary>
    public const DocumentType DefaultAllowedTypes =
        DocumentType.Pdf | DocumentType.Word | DocumentType.Excel | DocumentType.Image;

    public DocumentType AllowedDocumentTypes { get; set; } = DefaultAllowedTypes;

    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;

    public int MaxFilesPerComment { get; set; } = DefaultMaxFilesPerComment;

    public int? MaxFilesPerDay { get; set; } = DefaultMaxFilesPerDay;
}